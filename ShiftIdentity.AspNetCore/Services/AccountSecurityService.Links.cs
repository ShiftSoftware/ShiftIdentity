using System.Diagnostics;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.TypeAuth.Core;
using static ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.AdmissionRules;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

internal static partial class AccountSecurityService
{
    internal static async Task<AuthOutcome> RequestSecurityEmailAsync(IdentityAdmissionServices services,
        RequestSecurityEmail request, bool verification, string ingressKey, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await AtBoundary(async () =>
        {
            if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
            // Missing host delivery support is a global failure, independent of the supplied account.
            if (services.EmailSink is null) return Refuse(AuthenticationFailure.Unavailable);
            var digest = Convert.ToHexString(HMACSHA256.HashData(services.Options.OperationKey, Encoding.UTF8.GetBytes("delivery:" + ingressKey)));
            if (!await services.Store.ConsumeIngressAsync(digest, services.Clock.GetUtcNow(),
                services.DeliveryLimits.PublicPerIpPer15Minutes, TimeSpan.FromMinutes(15), ct)) return new SecurityDeliveryRequested();
            var lookup = await services.Store.ResolveSecurityEmailAsync(request.Identifier, ct);
            if (lookup is null) return new SecurityDeliveryRequested();
            services.Observe?.Invoke("SecurityEmailResolved");
            SecurityEmail? message = null;
            var outcome = await AtBoundary(() => services.Store.AdmitAsync<AuthOutcome>(lookup.UserID, null, services.Client, async unit =>
            {
                if (!await services.Store.RecheckSecurityEmailAsync(lookup, ct)) return new SecurityDeliveryRequested();
                return CreateSecurityLink(services, unit, verification ? AuthenticationOperationPurpose.EmailVerify : AuthenticationOperationPurpose.PasswordResetEmail, out message);
            }, ct));
            outcome = await HandoffAsync(services, outcome, lookup.UserID, message, ct);
            // A target-specific refusal or outage must not reveal that the identifier resolved to an account.
            return outcome is AuthenticationRefused ? new SecurityDeliveryRequested() : outcome;
        });
        // Include the entire bounded sender wait in every ordinary public response, even absent accounts.
        // SQL contention and cancellation can still affect timing; this is not a constant-time guarantee.
        var remaining = TimeSpan.FromMilliseconds(services.DeliveryLimits.HandoffTimeoutMilliseconds +
            services.DeliveryLimits.ResultPersistenceTimeoutMilliseconds + services.DeliveryLimits.PublicPaddingMilliseconds) - Stopwatch.GetElapsedTime(started);
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining, ct);
        return result;
    }

    internal static Task<AuthOutcome> RequestCurrentEmailVerificationAsync(IdentityAdmissionServices services,
        string? authorization, CancellationToken ct) => AtBoundary(async () =>
    {
        var signedIn = ReadSignedIn(services, authorization);
        if (signedIn is null) return Refuse(AuthenticationFailure.InvalidGrant);
        SecurityEmail? message = null;
        var outcome = await services.Store.AdmitAsync<AuthOutcome>(signedIn.Proof.UserID, null, services.Client, unit =>
        {
            var refusal = SignedInRefusal(services, unit, signedIn);
            return Task.FromResult<AuthOutcome>(refusal ?? CreateSecurityLink(services, unit, AuthenticationOperationPurpose.EmailVerify, out message, unit.User.ID));
        }, ct);
        return await HandoffAsync(services, outcome, signedIn.Proof.UserID, message, ct);
    });

    internal static Task<AuthOutcome> AdminSecurityLinkAsync(IdentityAdmissionServices services, string? authorization,
        long userID, AuthenticationOperationPurpose purpose, CancellationToken ct) => AtBoundary(async () =>
    {
        if (userID <= 0 || !IsSecurityLink(purpose)) return Refuse(AuthenticationFailure.InvalidRequest);
        var signedIn = ReadSignedIn(services, authorization);
        if (signedIn is null) return Refuse(AuthenticationFailure.InvalidGrant);
        SecurityEmail? message = null;
        var outcome = await services.Store.AdmitAdminAsync<AuthOutcome>(signedIn.Proof.UserID, userID, services.Client, (actor, unit) =>
        {
            var refusal = SignedInRefusal(services, actor, signedIn);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            var now = services.Clock.GetUtcNow();
            if (now < signedIn.Proof.AuthenticatedAt || now >= signedIn.Proof.AuthenticatedAt.AddMinutes(5) || LocalStep(actor, signedIn.Proof.MfaSatisfied) is not null)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidProof));
            var trees = actor.User.AccessTrees.Select(x => x.AccessTree.Tree).ToList();
            if (!string.IsNullOrWhiteSpace(actor.User.AccessTree)) trees.Add(actor.User.AccessTree);
            if (!new TypeAuthContext(trees, typeof(ShiftIdentityActions)).CanWrite(ShiftIdentityActions.Users))
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.ClientDenied));
            return Task.FromResult(CreateSecurityLink(services, unit, purpose, out message, actor.User.ID));
        }, ct);
        return await HandoffAsync(services, outcome, userID, message, ct);
    });

    private static AuthOutcome CreateSecurityLink(IdentityAdmissionServices services, IdentitySecurityTransaction unit,
        AuthenticationOperationPurpose purpose, out SecurityEmail? message, long? actorID = null)
    {
        message = null;
        var manual = purpose == AuthenticationOperationPurpose.PasswordResetManual;
        if (!manual && services.EmailSink is null) return Refuse(AuthenticationFailure.Unavailable);
        var verification = purpose == AuthenticationOperationPurpose.EmailVerify;
        var now = services.Clock.GetUtcNow();
        if (!unit.User.IsActive || unit.User.IsDeleted) return new SecurityDeliveryRequested();
        if (!RecoveryContact.LookupMatches(unit.User, unit.Security)) return new SecurityDeliveryRequested();
        if (unit.Policy.Revision != services.Options.PolicyRevision) return Refuse(AuthenticationFailure.Unavailable);
        var destination = unit.User.Email?.Trim();
        if (!manual && (!IsDeliveryAddress(destination) ||
            (verification ? unit.User.EmailVerified && RecoveryContact.IsEligible(unit.User, unit.Security) : !RecoveryContact.IsEligible(unit.User, unit.Security))))
            return new SecurityDeliveryRequested();
        // Manual grants share the issuance budget as well, preventing repeated privileged reissue from evading it.
        var security = unit.Security;
        if (security.LastDeliveryAt is { } last && now < last.AddSeconds(services.DeliveryLimits.CooldownSeconds))
            return new SecurityDeliveryRequested();
        if (security.DeliveryWindowStart is not { } start || now >= start.AddHours(1))
        { security.DeliveryWindowStart = now; security.DeliveryCount = 0; }
        if (now < security.DeliveryWindowStart || security.DeliveryCount >= services.DeliveryLimits.PerUserPerHour)
            return new SecurityDeliveryRequested();

        var slot = FormattableString.Invariant($"{unit.User.ID}:{(verification ? "verify" : "reset")}");
        foreach (var old in unit.Links.Where(x => x.OutstandingLinkSlot == slot))
        {
            AdmissionOperations.Finish(old, now, cancelled: true); old.State = AuthenticationOperationState.Superseded;
        }
        var credential = OperationCredential.Create(services.Options.OperationKey);
        var op = new AuthenticationOperation
        {
            ID = credential.ID, UserID = unit.User.ID, Purpose = purpose, State = AuthenticationOperationState.AwaitingExplicitSubmit,
            SecurityVersion = security.SecurityVersion, ContactRevision = security.ContactRevision, FactorGeneration = security.FactorGeneration,
            PolicyRevision = unit.Policy.Revision, ClientID = services.Client.ID, Audience = services.Client.Audience, External = services.Client.External,
            HandleDigest = credential.Digest, Destination = manual ? null : destination, OutstandingLinkSlot = slot,
            CreatedAt = now, ExpiresAt = verification ? now.AddHours(24) : now.AddMinutes(30)
        };
        unit.AddOperation(op);
        security.LastDeliveryAt = now; security.DeliveryCount++;
        if (!manual)
        {
            message = new(op.ID, destination!, verification ? "Verify your email" : "Reset your password",
                credential.Handle, purpose, op.ExpiresAt);
        }
        unit.Audit(manual ? "ManualPasswordResetIssued" : verification ? "EmailVerificationRequested" : "PasswordResetRequested", now, op.ID, actorID);
        services.Observe?.Invoke("SecurityLinkPrepared");
        return manual ? new ManualPasswordResetIssued(credential.Handle, Mask(unit.User.Username), op.ExpiresAt) : new SecurityDeliveryRequested();
    }

    private static async Task<AuthOutcome> HandoffAsync(IdentityAdmissionServices services, AuthOutcome outcome,
        long userID, SecurityEmail? message, CancellationToken ct)
    {
        // Do not send if persistence failed, including an unconfirmed SQL commit response.
        if (outcome is not SecurityDeliveryRequested || message is null) return outcome;
        var accepted = false;
        using (var handoff = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            handoff.CancelAfter(TimeSpan.FromMilliseconds(services.DeliveryLimits.HandoffTimeoutMilliseconds));
            try
            {
                handoff.Token.ThrowIfCancellationRequested();
                // The grant transaction has committed. No SQL transaction or account lock spans host I/O.
                await services.EmailSink!.DeliverAsync(message, handoff.Token).WaitAsync(handoff.Token);
                accepted = true;
            }
            catch (Exception)
            {
                // Host acceptance is unconfirmed. Never retry or log exceptions that may contain secrets.
            }
        }
        // A disconnected caller must not prevent bounded cleanup. A SQL outage can leave the grant
        // usable until expiry or supersession; that is safe uncertainty, not transactional email.
        using var cleanup = new CancellationTokenSource(TimeSpan.FromMilliseconds(services.DeliveryLimits.ResultPersistenceTimeoutMilliseconds));
        try
        {
            await services.Store.AdmitAsync(userID, message.ID, services.Client, unit =>
            {
                var op = unit.Operation;
                if (op is null) return Task.FromResult(false);
                var now = services.Clock.GetUtcNow();
                // A late failure can only cancel this pending grant, never a newer or completed one.
                if (!accepted && op.State == AuthenticationOperationState.AwaitingExplicitSubmit)
                    AdmissionOperations.Finish(op, now, cancelled: true);
                unit.Audit(accepted ? "SecurityDeliveryAccepted" : "SecurityDeliveryUnconfirmed", now, op.ID);
                return Task.FromResult(true);
            }, cleanup.Token);
        }
        catch (IdentitySecurityUnavailableException) { }
        catch (IdentitySecurityConflictException) { }
        catch (OperationCanceledException) { }
        return accepted ? new SecurityDeliveryRequested() : Refuse(AuthenticationFailure.Unavailable);
    }

    internal static Task<AuthOutcome> OpenSecurityLinkAsync(IdentityAdmissionServices services, OpenSecurityLinkRequest request,
        CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request) || !IsSecurityLink(request.Purpose)) return Refuse(AuthenticationFailure.InvalidGrant);
        var reference = await AdmissionOperations.ReadAsync(services, request.Grant, request.Purpose, ct);
        if (reference is null) return Refuse(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
        {
            var refusal = CheckSecurityLink(services, unit, reference);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            var op = unit.Operation!;
            var expires = Min(op.ExpiresAt, services.Clock.GetUtcNow().AddMinutes(10));
            var page = new LinkPage(request.Grant, op.Purpose, services.Client.ID, services.Client.Audience, services.Client.External, expires);
            var handle = services.LinkProtector.CreateProtector("Page").Protect(JsonSerializer.Serialize(page));
            // No operation transition, audit event, rotation or consumption on a scanner's visit.
            return Task.FromResult<AuthOutcome>(new SecurityLinkOpened(handle, Mask(op.Destination ?? unit.User.Username), op.Purpose, expires));
        }, ct);
    });

    internal static Task<AuthOutcome> CompletePasswordResetAsync(IdentityAdmissionServices services,
        CompletePasswordResetRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var reference = await ReadPageAsync(services, request.PageHandle, ct, AuthenticationOperationPurpose.PasswordResetEmail, AuthenticationOperationPurpose.PasswordResetManual);
        if (reference is null) return Refuse(AuthenticationFailure.InvalidGrant);
        var read = await services.Store.AdmitAsync(reference.UserID, reference.ID, services.Client, unit =>
        {
            var failure = CheckSecurityLink(services, unit, reference);
            return Task.FromResult(new SnapshotResult(failure is null ? new(unit.User, unit.Security, unit.Policy) : null, failure));
        }, ct);
        if (read.Failure is not null) return read.Failure;
        var snapshot = read.Snapshot!;
        var failure = services.PasswordPolicy.Validate(request.NewPassword, snapshot.User.Username);
        if (failure is not null) return await RefuseResetPasswordAsync(services, reference, failure.Value, ct);
        if (HashService.VerifyVersionedPassword(request.NewPassword, snapshot.User.Salt, snapshot.User.PasswordHash))
            return await RefuseResetPasswordAsync(services, reference, PasswordPolicyFailure.SameAsCurrent, ct);
        var candidate = HashService.GenerateVersionedHash(request.NewPassword);
        services.Observe?.Invoke("ResetPasswordPrepared");
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
        {
            var refusal = CheckSecurityLink(services, unit, reference);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            if (!SameCredential(snapshot, unit)) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidGrant));
            var op = unit.Operation!;
            // Page expiry also bounds an expensive password hash that was prepared outside the lock.
            if (!ValidPage(services, request.PageHandle, out _)) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidGrant));
            unit.User.PasswordHash = candidate.PasswordHash; unit.User.Salt = candidate.Salt; unit.User.RequireChangePassword = false;
            unit.Security.SecurityVersion = checked(unit.Security.SecurityVersion + 1);
            if (op.Purpose == AuthenticationOperationPurpose.PasswordResetEmail)
            {
                unit.User.EmailVerified = true;
                RecoveryContact.RecordOwnership(unit.User, unit.Security, RecoveryEmailProvenance.OwnershipVerification);
            }
            var now = services.Clock.GetUtcNow();
            AdmissionOperations.Finish(op, now);
            unit.Audit("PasswordResetCompleted", now, op.ID);
            services.Observe?.Invoke("ResetPasswordMutation");
            return Task.FromResult<AuthOutcome>(new ReturnToLogin());
        }, ct);
    });

    internal static Task<AuthOutcome> CompleteEmailVerificationAsync(IdentityAdmissionServices services,
        CompleteEmailVerificationRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var reference = await ReadPageAsync(services, request.PageHandle, ct, AuthenticationOperationPurpose.EmailVerify);
        if (reference is null) return Refuse(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
        {
            var refusal = CheckSecurityLink(services, unit, reference);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            if (!ValidPage(services, request.PageHandle, out _)) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidGrant));
            unit.User.EmailVerified = true;
            RecoveryContact.RecordOwnership(unit.User, unit.Security, RecoveryEmailProvenance.OwnershipVerification);
            var now = services.Clock.GetUtcNow();
            AdmissionOperations.Finish(unit.Operation!, now);
            unit.Audit("EmailVerified", now, reference.ID);
            services.Observe?.Invoke("EmailVerificationMutation");
            return Task.FromResult<AuthOutcome>(new EmailVerificationCompleted());
        }, ct);
    });

    internal static AuthenticationRefused? CheckSecurityLink(IdentityAdmissionServices services, IdentitySecurityTransaction unit,
        AdmissionOperations.Reference reference)
    {
        var op = unit.Operation;
        var now = services.Clock.GetUtcNow();
        if (op is null || !IsSecurityLink(op.Purpose) || op.ID != reference.ID || op.UserID != unit.User.ID || op.Purpose != reference.Purpose ||
            op.State != AuthenticationOperationState.AwaitingExplicitSubmit || op.OutstandingLinkSlot is null || op.FailedAttempts >= 5 ||
            !CryptographicOperations.FixedTimeEquals(op.HandleDigest, reference.Digest) ||
            op.ClientID != services.Client.ID || op.Audience != services.Client.Audience || op.External != services.Client.External ||
            now < op.CreatedAt || now >= op.ExpiresAt || !unit.User.IsActive || unit.User.IsDeleted ||
            !RecoveryContact.LookupMatches(unit.User, unit.Security) ||
            op.SecurityVersion != unit.Security.SecurityVersion || op.ContactRevision != unit.Security.ContactRevision ||
            op.FactorGeneration != unit.Security.FactorGeneration || op.PolicyRevision != unit.Policy.Revision ||
            (op.Purpose != AuthenticationOperationPurpose.PasswordResetManual &&
                (op.Destination != unit.User.Email?.Trim() || !IsDeliveryAddress(op.Destination))) ||
            (op.Purpose == AuthenticationOperationPurpose.PasswordResetEmail && !RecoveryContact.IsEligible(unit.User, unit.Security)))
            return Refuse(AuthenticationFailure.InvalidGrant);
        return services.Options.PolicyRevision != unit.Policy.Revision ? Refuse(AuthenticationFailure.Unavailable) : null;
    }

    private static Task<AuthOutcome> RefuseResetPasswordAsync(IdentityAdmissionServices services, AdmissionOperations.Reference reference,
        PasswordPolicyFailure failure, CancellationToken ct) => services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
    {
        var refusal = CheckSecurityLink(services, unit, reference);
        if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
        var op = unit.Operation!;
        op.FailedAttempts++;
        if (op.FailedAttempts >= 5)
        {
            AdmissionOperations.Finish(op, services.Clock.GetUtcNow(), cancelled: true);
            op.State = AuthenticationOperationState.Locked;
        }
        unit.Audit("ResetPasswordPolicyRefused", services.Clock.GetUtcNow(), op.ID);
        return Task.FromResult<AuthOutcome>(new AuthenticationRefused(AuthenticationFailure.InvalidNewPassword, failure));
    }, ct);

    private static bool IsDeliveryAddress(string? value) => value is { Length: > 3 and <= 255 } &&
        !value.Any(char.IsControl) && MailAddress.TryCreate(value, out var mail) && mail.Address == value && string.IsNullOrEmpty(mail.DisplayName);
    private static bool IsSecurityLink(AuthenticationOperationPurpose purpose) => purpose is AuthenticationOperationPurpose.PasswordResetEmail or AuthenticationOperationPurpose.PasswordResetManual or AuthenticationOperationPurpose.EmailVerify;
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    private static string Mask(string value)
    {
        var at = value.IndexOf('@');
        return value.Length == 0 ? "account" : at > 0 ? value[..1] + "***" + value[at..] : value[..1] + "***";
    }
    private sealed record LinkPage(string Grant, AuthenticationOperationPurpose Purpose, string ClientID, string Audience, bool External, DateTimeOffset ExpiresAt);
    private static bool ValidPage(IdentityAdmissionServices services, string handle, out LinkPage? page)
    {
        page = null;
        try
        {
            page = JsonSerializer.Deserialize<LinkPage>(services.LinkProtector.CreateProtector("Page").Unprotect(handle));
            return page is not null && page.ClientID == services.Client.ID && page.Audience == services.Client.Audience &&
                page.External == services.Client.External && services.Clock.GetUtcNow() < page.ExpiresAt;
        }
        catch (CryptographicException) { return false; }
        catch (JsonException) { return false; }
        catch (FormatException) { return false; }
    }
    private static async Task<AdmissionOperations.Reference?> ReadPageAsync(IdentityAdmissionServices services, string handle,
        CancellationToken ct, params AuthenticationOperationPurpose[] purposes) =>
        ValidPage(services, handle, out var page) && purposes.Contains(page!.Purpose)
            ? await AdmissionOperations.ReadAsync(services, page.Grant, page.Purpose, ct) : null;

}
