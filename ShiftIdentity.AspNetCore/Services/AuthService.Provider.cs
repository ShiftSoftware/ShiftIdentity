using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using static ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.AdmissionRules;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

// The provider proved an account; the browser completes with this handle and its verifier. Never a v2 wire outcome.
internal sealed record ProviderProven(string Handle) : AuthOutcome;

public partial class AuthService
{
    private const string ProviderStatePurpose = "ProviderSignIn.v1";

    // What the browser carries to the provider and back, encrypted: the login screen's challenge, the token's expected
    // nonce, this server's PKCE verifier for the provider, a deadline, the client and the provider it was started for
    // (so one provider's return cannot complete at another's callback). Nothing in it is readable on the way.
    private sealed record ProviderState(string Challenge, string Nonce, string Verifier, long ExpiresAt, string Client,
        SignInProvider Provider = SignInProvider.Microsoft);

    /// <summary>Starts a provider sign-in for the login screen's challenge. Nothing is stored until the provider returns.</summary>
    internal static AuthOutcome StartProviderSignIn(IdentityAdmissionServices services, SignInProvider provider,
        StartProviderSignInRequest request, string redirectUri)
    {
        if (services.Provider(provider) is not { } signIn) return Refuse(AuthenticationFailure.ClientDenied);
        if (!Valid(request) || !OperationCredential.IsChallenge(request.CodeChallenge)) return Refuse(AuthenticationFailure.InvalidRequest);
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var nonce = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var state = new ProviderState(request.CodeChallenge, nonce, verifier,
            services.Clock.GetUtcNow().AddMinutes(10).ToUnixTimeSeconds(), services.Client.ID, provider);
        var protectedState = services.FactorProtector.CreateProtector(ProviderStatePurpose).Protect(JsonSerializer.Serialize(state));
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));
        return new ProviderRedirect(signIn.AuthorizeUrl(protectedState, nonce, challenge, redirectUri));
    }

    /// <summary>
    /// The provider's return. A verified email that matches an existing active account (or that account's earlier link)
    /// proves the account: the link is recorded, the email marked verified and a completion handle bound to the login
    /// screen's challenge is created. No session is issued here; the browser completes with its verifier.
    /// </summary>
    internal static Task<AuthOutcome> ProviderCallbackAsync(IdentityAdmissionServices services, SignInProvider provider,
        string? code, string? state, string? error, string redirectUri, CancellationToken ct) => AtBoundary(async () =>
    {
        if (services.Provider(provider) is not { } signIn) return Refuse(AuthenticationFailure.ClientDenied);
        if (ReadProviderState(services, state) is not { } payload || payload.Provider != provider) return Refuse(AuthenticationFailure.InvalidGrant);
        var now = services.Clock.GetUtcNow();
        if (now >= DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAt)) return Refuse(AuthenticationFailure.Expired);
        // The user cancelled, or the provider refused: nothing to redeem.
        if (error is not null || code is not { Length: > 0 and <= 4096 }) return Refuse(AuthenticationFailure.InvalidGrant);
        ProviderIdentity? identity;
        try { identity = await signIn.Tokens.RedeemAsync(code, payload.Verifier, redirectUri, payload.Nonce, ct); }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException)
        { return Refuse(AuthenticationFailure.Unavailable); }
        if (identity is null) return Refuse(AuthenticationFailure.InvalidGrant);
        if (!identity.EmailVerified || identity.Email is null) return Refuse(AuthenticationFailure.ProviderEmailUnverified);
        var key = RecoveryContact.Key(identity.Email);
        var userID = await services.Store.FindProviderUserAsync(provider, identity.Directory, identity.Subject, key, ct);
        if (userID is null) return Refuse(AuthenticationFailure.ProviderAccountNotFound);
        SecurityEmail? notice = null;
        var outcome = await services.Store.AdmitAsync<AuthOutcome>(userID.Value, null, services.Client, async unit =>
        {
            if (!unit.User.IsActive || unit.User.IsDeleted) return Refuse(AuthenticationFailure.AccountUnavailable);
            if (unit.Policy.Revision != services.Options.PolicyRevision) return Refuse(AuthenticationFailure.Unavailable);
            if (!RecoveryContact.LookupMatches(unit.User, unit.Security) || unit.Security.EmailLookupKey is not { } accountKey)
                return Refuse(AuthenticationFailure.ProviderAccountNotFound);
            var link = await services.Store.ReadProviderLinkAsync(provider, identity.Directory, identity.Subject, ct);
            var admittedAt = services.Clock.GetUtcNow();
            if (link is not null && link.UserID == unit.User.ID && link.EmailLookupKey == accountKey)
            {
                link.LastUsedAt = admittedAt;
                link.PersonalAccount = identity.PersonalAccount;
            }
            else
            {
                // First sign-in of this identity to this account, or its link went stale with an email change: the email
                // the provider verified must be the account's own. A stale row is reused, so the identity stays unique.
                if (accountKey != key) return Refuse(AuthenticationFailure.ProviderAccountNotFound);
                if (link is null)
                {
                    link = new UserProviderLink { ID = Guid.NewGuid(), Provider = provider,
                        TenantID = identity.Directory, ObjectID = identity.Subject };
                    services.Store.AddProviderLink(link);
                }
                link.UserID = unit.User.ID; link.EmailLookupKey = accountKey; link.Email = identity.Email;
                link.PersonalAccount = identity.PersonalAccount;
                link.CreatedAt = admittedAt; link.LastUsedAt = admittedAt;
                unit.Audit("ProviderLinked", admittedAt);
                notice = new SecurityEmail(link.ID, unit.User.Email!.Trim(),
                    (provider == SignInProvider.Google ? "Google" : "Microsoft") + " sign-in linked to your account", "",
                    AuthenticationOperationPurpose.ProviderLogin, admittedAt)
                {
                    UserID = services.HashIds.Encode<Core.DTOs.User.UserDTO>(unit.User.ID), Username = unit.User.Username,
                    FullName = unit.User.FullName, ProviderAccount = identity.Email, Provider = provider
                };
            }
            // Signing in with the provider as this address proves its owner has it, as completing a verification link does.
            unit.User.EmailVerified = true;
            RecoveryContact.RecordOwnership(unit.User, unit.Security, RecoveryEmailProvenance.OwnershipVerification);
            var credential = OperationCredential.Create(services.Options.OperationKey);
            var op = new AuthenticationOperation
            {
                ID = credential.ID, UserID = unit.User.ID, Purpose = AuthenticationOperationPurpose.ProviderLogin,
                State = AuthenticationOperationState.AwaitingProviderCompletion,
                SecurityVersion = unit.Security.SecurityVersion, FactorGeneration = unit.Security.FactorGeneration,
                PolicyRevision = unit.Policy.Revision, ClientID = services.Client.ID, Audience = services.Client.Audience,
                External = services.Client.External, HandleDigest = credential.Digest, CodeChallenge = payload.Challenge,
                CreatedAt = admittedAt, ExpiresAt = admittedAt.AddMinutes(5), PasswordProvenAt = admittedAt,
                SessionProvider = provider
            };
            unit.AddOperation(op);
            unit.Audit("ProviderProven", admittedAt, op.ID);
            return new ProviderProven(credential.Handle);
        }, ct);
        if (outcome is ProviderProven && notice is not null) await NotifyProviderLinkAsync(services, notice);
        return outcome;
    });

    /// <summary>
    /// Completes a provider sign-in on the login screen that started it (its verifier). Issues the session, or continues
    /// into the Shift MFA step where the host's provider settings ask for one.
    /// </summary>
    internal static Task<AuthOutcome> CompleteProviderSignInAsync(IdentityAdmissionServices services,
        CompleteProviderSignInRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var reference = await AdmissionOperations.ReadAsync(services, request.Handle, AuthenticationOperationPurpose.ProviderLogin, ct);
        if (reference is null) return Refuse(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
        {
            var refusal = AdmissionOperations.Check(services, unit, reference, request.CodeVerifier,
                AuthenticationOperationPurpose.ProviderLogin, AuthenticationOperationState.AwaitingProviderCompletion);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            var op = unit.Operation!;
            if (op.SessionProvider is null) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidGrant));
            if (ProviderRefusal(services, op.SessionProvider) is { } providerRefusal) return Task.FromResult<AuthOutcome>(providerRefusal);
            var now = services.Clock.GetUtcNow();
            var challenge = op.CodeChallenge;
            var provenAt = op.PasswordProvenAt!.Value;
            var provider = op.SessionProvider;
            var step = SessionStep(services, unit, provider, false);
            AdmissionOperations.Finish(op, now);
            unit.User.UserLog ??= new Data.Entities.UserLog();
            unit.User.UserLog.LastSeen = now;
            return Task.FromResult<AuthOutcome>(step switch
            {
                null => Issue(services, unit, Proof(unit, services, false, provenAt), now),
                // The step runs from the provider's proof, as a password sign-in's step runs from its password proof.
                AuthenticationStep.ExistingMfa => AdmissionOperations.Create(services, unit, AuthenticationOperationPurpose.Login,
                    AuthenticationOperationState.AwaitingMfa, challenge, provenAt, provenAt.AddMinutes(5), passwordProvenAt: provenAt, sessionProvider: provider),
                AuthenticationStep.NewMfa => AdmissionOperations.Create(services, unit, AuthenticationOperationPurpose.MfaEnrollment,
                    AuthenticationOperationState.AwaitingNewFactor, challenge, provenAt, provenAt.AddMinutes(10), passwordProvenAt: provenAt,
                    prepareNewFactor: true, sessionProvider: provider),
                { } other => Restricted(other, now)
            });
        }, ct);
    });

    /// <summary>
    /// The provider accounts that can sign in to the signed-in account, or, with <paramref name="userKey"/>, to that
    /// user for an operator who can read users. Only links the account's current email still holds are listed: a stale
    /// link signs in nothing.
    /// </summary>
    internal static Task<AuthOutcome> ReadProviderLinksAsync(IdentityAdmissionServices services, string? authorization,
        string? userKey, CancellationToken ct) => AtBoundary(async () =>
    {
        var signedIn = ReadSignedIn(services, authorization);
        if (signedIn is null || signedIn.Proof.External) return Refuse(AuthenticationFailure.InvalidGrant);
        long target = signedIn.Proof.UserID;
        if (userKey is not null)
        {
            try { target = services.HashIds.Decode<Core.DTOs.User.UserDTO>(userKey); }
            catch (Exception e) when (e is ArgumentException or FormatException or OverflowException) { target = 0; }
            if (target <= 0) return Refuse(AuthenticationFailure.InvalidRequest);
        }
        string? emailKey = null;
        var refusal = await services.Store.AdmitAdminAsync<AuthenticationRefused?>(signedIn.Proof.UserID, target, services.Client, (actor, unit) =>
        {
            var refused = target == signedIn.Proof.UserID ? SignedInRefusal(services, actor, signedIn)
                : AccountSecurityService.ActorRefusal(services, actor, signedIn,
                    permissions => permissions.CanRead(Core.ShiftIdentityActions.Users), requireRecent: false);
            emailKey = unit.Security.EmailLookupKey;
            return Task.FromResult(refused);
        }, ct);
        if (refusal is not null) return refusal;
        var links = await services.Store.ReadProviderLinksAsync(target, ct);
        return new ProviderLinksRead(links.Where(x => emailKey is not null && x.EmailLookupKey == emailKey)
            .Select(x => new ProviderLinkView(x.Provider, x.Email, x.PersonalAccount, x.CreatedAt, x.LastUsedAt))
            .ToList());
    });

    private static ProviderState? ReadProviderState(IdentityAdmissionServices services, string? state)
    {
        if (state is not { Length: > 0 and <= 2048 }) return null;
        try
        {
            var payload = JsonSerializer.Deserialize<ProviderState>(services.FactorProtector.CreateProtector(ProviderStatePurpose).Unprotect(state));
            return payload is not null && payload.Client == services.Client.ID && OperationCredential.IsChallenge(payload.Challenge) ? payload : null;
        }
        catch (Exception e) when (e is CryptographicException or FormatException or JsonException) { return null; }
    }

    // A notice, not a grant: best effort after the commit, bounded like any handoff, and never a reason to refuse the sign-in.
    private static async Task NotifyProviderLinkAsync(IdentityAdmissionServices services, SecurityEmail notice)
    {
        if (services.EmailSink is not { } sink) return;
        using var budget = new CancellationTokenSource(services.DeliveryLimits.HandoffTimeoutMilliseconds);
        try { await sink.DeliverAsync(notice, budget.Token).WaitAsync(budget.Token); }
        catch (Exception) { services.Observe?.Invoke("ProviderNoticeUnconfirmed"); }
    }
}
