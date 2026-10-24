using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using static ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.AdmissionRules;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

internal static partial class AccountSecurityService
{
    internal static Task<AuthOutcome> BeginAdministratorConfirmationAsync(IdentityAdmissionServices services,
        string authorization, StartPasswordChangeRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request) || !OperationCredential.IsChallenge(request.CodeChallenge)) return Refuse(AuthenticationFailure.InvalidRequest);
        var session = ReadSignedIn(services, authorization);
        if (session is null || session.Proof.External) return Refuse(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(session.Proof.UserID, null, services.Client, unit =>
        {
            var refusal = ConfirmationSessionRefusal(services, unit, session);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            var now = services.Clock.GetUtcNow();
            var deadline = now.AddMinutes(5) < session.ExpiresAt ? now.AddMinutes(5) : session.ExpiresAt;
            // An account held to a trusted tenant has no password: it confirms with a fresh Microsoft sign-in instead.
            var microsoftOnly = RequiredTenant(services, unit.User) is not null;
            if (microsoftOnly && services.Microsoft is null) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.ClientDenied));
            var challenge = AdmissionOperations.Create(services, unit, AuthenticationOperationPurpose.AdministratorConfirmation,
                microsoftOnly ? AuthenticationOperationState.AwaitingProviderCompletion : AuthenticationOperationState.AwaitingPassword,
                request.CodeChallenge, now, deadline, sourceSessionDigest: SessionDigest(authorization));
            // The provider that can also confirm it with a fresh sign-in, for the page to offer beside (or instead of) the password.
            return Task.FromResult<AuthOutcome>(ConfirmingProvider(services, unit, session) is { } provider
                ? challenge with { Challenge = challenge.Challenge with { Provider = provider } } : challenge);
        }, ct);
    });

    /// <summary>
    /// The provider whose fresh sign-in confirms this administrator: Microsoft for an account held to a trusted tenant,
    /// otherwise the provider the session being confirmed came from, while the host has it on. Null for a password
    /// session: it confirms with the password. A provider already signs such an account in without its password, so a
    /// new sign-in there proves the person at the keyboard as well as the password does (owner, 24 October 2026).
    /// The new sign-in must be provable, so the token must say when it happened (<c>auth_time</c>). Personal Microsoft
    /// accounts never say, so a Microsoft session counts only from a work or school tenant: not the personal-account
    /// tenant, a guest, or a session from before sessions carried a tenant. Those confirm with the password (owner,
    /// 25 October 2026).
    /// </summary>
    private static SignInProvider? ConfirmingProvider(IdentityAdmissionServices services, IdentitySecurityTransaction unit, SignedInContext session) =>
        RequiredTenant(services, unit.User) is not null ? services.Microsoft is null ? null : SignInProvider.Microsoft
            : session.Proof.Provider is not { } provider || services.Provider(provider) is null ? null
            : provider == SignInProvider.Microsoft && (session.Proof.Tenant is null || session.Proof.Tenant == MicrosoftSignIn.ConsumerTenant) ? null
            : provider;

    /// <summary>
    /// Opens the provider sign-in that proves an administrator confirmation: the popup's authorize URL, bound to this
    /// confirmation. The provider returns to its callback, which records the proof on the confirmation; this page then
    /// completes it with <see cref="CompleteAdministratorProviderAsync"/>. The password stays available meanwhile, except
    /// for an account held to a trusted tenant, which has none.
    /// </summary>
    internal static Task<AuthOutcome> StartAdministratorProviderAsync(IdentityAdmissionServices services, HttpRequest http,
        string authorization, AdministratorProviderConfirmationRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var session = ReadSignedIn(services, authorization);
        if (session is null) return Refuse(AuthenticationFailure.InvalidGrant);
        var reference = await AdmissionOperations.ReadAsync(services, request.Handle, AuthenticationOperationPurpose.AdministratorConfirmation, ct);
        if (reference is null || reference.UserID != session.Proof.UserID) return Refuse(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
        {
            var failure = CheckConfirmation(services, unit, session, authorization, reference, request.CodeVerifier,
                AuthenticationOperationState.AwaitingPassword, AuthenticationOperationState.AwaitingProviderCompletion);
            if (failure is not null) return Task.FromResult<AuthOutcome>(failure);
            var op = unit.Operation!;
            if (op.PasswordProvenAt is not null) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
            if (ConfirmingProvider(services, unit, session) is not { } provider || services.Provider(provider) is not { } signIn)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.ClientDenied));
            var email = unit.User.Email?.Trim();
            var domain = email is null || provider != SignInProvider.Microsoft || RequiredTenant(services, unit.User) is null
                ? null : email[(email.LastIndexOf('@') + 1)..];
            return Task.FromResult<AuthOutcome>(new ProviderRedirect(AuthService.ProviderConfirmationUrl(services, signIn, op.ID,
                op.CodeChallenge, op.ExpiresAt, signIn.CallbackUri(http), email, domain)));
        }, ct);
    });

    /// <summary>
    /// The provider's return to a confirmation popup. A fresh sign-in by the account's own provider account (linked to it,
    /// or with its email), and for an account held to a trusted tenant only a Microsoft account of that tenant, proves the
    /// confirmation: the proof is recorded on it for the page to complete. Never issues anything; the popup's page only
    /// learns whether it worked.
    /// </summary>
    internal static async Task<AuthOutcome> ProviderConfirmationCallbackAsync(IdentityAdmissionServices services, ProviderSignIn signIn,
        Guid confirmation, string nonce, string verifier, DateTimeOffset expiresAt, string? code, string? error, string redirectUri,
        CancellationToken ct)
    {
        static AuthOutcome Returned(AuthenticationFailure? failure) => new ProviderConfirmationReturned(failure);
        if (services.Clock.GetUtcNow() >= expiresAt) return Returned(AuthenticationFailure.Expired);
        if (error is not null || code is not { Length: > 0 and <= 4096 }) return Returned(AuthenticationFailure.InvalidGrant);
        ProviderIdentity? identity;
        try { identity = await signIn.Tokens.RedeemAsync(code, verifier, redirectUri, nonce, ct); }
        catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException or InvalidOperationException)
        { return Returned(AuthenticationFailure.Unavailable); }
        if (identity is null) return Returned(AuthenticationFailure.InvalidGrant);
        if (!identity.EmailVerified || identity.Email is null) return Returned(AuthenticationFailure.ProviderEmailUnverified);
        var provenAt = services.Clock.GetUtcNow();
        // The popup asks the provider for a new sign-in (prompt=login, max_age=0), but the browser carries that request and
        // can drop it, and the provider then answers from its own session. Only the token's auth_time shows the sign-in
        // happened now, so it is required (review, 25 October 2026): Google sends it when the request asks for it,
        // Microsoft when the app registration adds the auth_time optional claim to the ID token.
        if (identity.AuthenticatedAt is not { } authenticated)
        {
            services.Observe?.Invoke("ProviderConfirmationWithoutAuthTime");
            return Returned(AuthenticationFailure.InvalidGrant);
        }
        if (authenticated < provenAt.AddMinutes(-5).AddSeconds(-30) || authenticated > provenAt.AddSeconds(30))
            return Returned(AuthenticationFailure.Expired);
        var found = await services.Store.ReadOperationAsync(confirmation, ct);
        if (found is null || found.Purpose != AuthenticationOperationPurpose.AdministratorConfirmation) return Returned(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(found.UserID, confirmation, services.Client, async unit =>
        {
            var op = unit.Operation;
            var now = services.Clock.GetUtcNow();
            if (op is null || op.ID != confirmation || op.UserID != unit.User.ID || op.Purpose != AuthenticationOperationPurpose.AdministratorConfirmation ||
                op.State is not (AuthenticationOperationState.AwaitingPassword or AuthenticationOperationState.AwaitingProviderCompletion) ||
                op.PasswordProvenAt is not null ||
                op.ClientID != services.Client.ID || op.Audience != services.Client.Audience || op.External != services.Client.External)
                return Returned(AuthenticationFailure.InvalidGrant);
            if (now >= op.ExpiresAt || now < op.CreatedAt) return Returned(AuthenticationFailure.Expired);
            if (CommonRefusal(services, unit, op.SecurityVersion, op.PolicyRevision) is { } common) return Returned(common.Code);
            if (unit.Security.FactorGeneration != op.FactorGeneration) return Returned(AuthenticationFailure.StaleOperation);
            // An account held to a trusted tenant takes only a Microsoft account of that tenant; any account takes only
            // its own provider account: the one linked to it, or one with its email.
            var provider = signIn.Provider;
            var tenant = identity.SessionTenant(provider);
            if (SignInMethodRefusal(services, unit.User, provider, tenant) is { } method) return Returned(method.Code);
            if (!RecoveryContact.LookupMatches(unit.User, unit.Security) || unit.Security.EmailLookupKey is not { } accountKey)
                return Returned(AuthenticationFailure.ProviderAccountNotFound);
            var link = await services.Store.ReadProviderLinkAsync(provider, identity.Directory, identity.Subject, ct);
            var linked = link is not null && link.UserID == unit.User.ID && link.EmailLookupKey == accountKey;
            if ((link is not null && link.UserID != unit.User.ID) || (!linked && RecoveryContact.Key(identity.Email) != accountKey))
                return Returned(AuthenticationFailure.ProviderAccountNotFound);
            if (linked) link!.LastUsedAt = now;
            op.PasswordProvenAt = now;
            op.SessionProvider = provider;
            op.SessionTenant = tenant;
            op.State = AuthenticationOperationState.AwaitingProviderCompletion;
            unit.Audit("AdministratorProviderProven", now, op.ID);
            return Returned(null);
        }, ct);
    }

    /// <summary>
    /// Completes an administrator confirmation that Microsoft proved: issues the confirmed session, or continues into the
    /// Shift MFA step where the tenant does not skip it. Refused with <see cref="AuthenticationFailure.AuthorizationPending"/>
    /// while Microsoft has not returned yet, so the page can ask again.
    /// </summary>
    internal static Task<AuthOutcome> CompleteAdministratorProviderAsync(IdentityAdmissionServices services,
        string authorization, AdministratorProviderConfirmationRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var session = ReadSignedIn(services, authorization);
        if (session is null) return Refuse(AuthenticationFailure.InvalidGrant);
        var reference = await AdmissionOperations.ReadAsync(services, request.Handle, AuthenticationOperationPurpose.AdministratorConfirmation, ct);
        if (reference is null || reference.UserID != session.Proof.UserID) return Refuse(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
        {
            // Before the provider returns, a confirmation that also takes the password still waits for it.
            var failure = CheckConfirmation(services, unit, session, authorization, reference, request.CodeVerifier,
                AuthenticationOperationState.AwaitingPassword, AuthenticationOperationState.AwaitingProviderCompletion);
            if (failure is not null) return Task.FromResult<AuthOutcome>(failure);
            var op = unit.Operation!;
            if (op.PasswordProvenAt is null || op.SessionProvider is null) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.AuthorizationPending));
            return Task.FromResult<AuthOutcome>(ConfirmationStep(services, unit, false) switch
            {
                null => CompleteAdministratorConfirmation(services, unit),
                AuthenticationStep.ExistingMfa => AdmissionOperations.Advance(services, op, AuthenticationOperationState.AwaitingMfa),
                _ => Refuse(AuthenticationFailure.InvalidProof)
            });
        }, ct);
    });

    // The step the confirmation still owes: a password confirmation owes the local steps, a Microsoft one its session's.
    private static AuthenticationStep? ConfirmationStep(IdentityAdmissionServices services, IdentitySecurityTransaction unit, bool mfaSatisfied) =>
        unit.Operation?.SessionProvider is { } provider
            ? SessionStep(services, unit, provider, mfaSatisfied, unit.Operation.SessionTenant)
            : LocalStep(unit, mfaSatisfied);

    internal static Task<AuthOutcome> ProveAdministratorPasswordAsync(IdentityAdmissionServices services,
        string authorization, AdministratorPasswordProofRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var session = ReadSignedIn(services, authorization);
        if (session is null) return Refuse(AuthenticationFailure.InvalidGrant);
        var reference = await AdmissionOperations.ReadAsync(services, request.Handle, AuthenticationOperationPurpose.AdministratorConfirmation, ct);
        if (reference is null || reference.UserID != session.Proof.UserID) return Refuse(AuthenticationFailure.InvalidGrant);
        var read = await services.Store.AdmitAsync(reference.UserID, reference.ID, services.Client, unit =>
        {
            var failure = CheckConfirmation(services, unit, session, authorization, reference, request.CodeVerifier, AuthenticationOperationState.AwaitingPassword);
            return Task.FromResult(new SnapshotResult(failure is null ? new(unit.User, unit.Security, unit.Policy) : null, failure));
        }, ct);
        if (read.Failure is not null) return read.Failure;
        var snapshot = read.Snapshot!;
        var valid = HashService.VerifyVersionedPassword(request.CurrentPassword, snapshot.User.Salt, snapshot.User.PasswordHash);
        var provenAt = services.Clock.GetUtcNow();
        services.Observe?.Invoke("AdministratorPasswordProof");
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
        {
            var failure = CheckConfirmation(services, unit, session, authorization, reference, request.CodeVerifier, AuthenticationOperationState.AwaitingPassword);
            if (failure is not null) return Task.FromResult<AuthOutcome>(failure);
            if (!SameCredential(snapshot, unit)) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
            if (!valid)
            {
                AdmissionOperations.FailedAttempt(unit, services.Clock.GetUtcNow(), "InvalidPassword");
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidProof));
            }
            unit.Operation!.PasswordProvenAt = provenAt;
            return Task.FromResult(LocalStep(unit, false) == AuthenticationStep.ExistingMfa
                ? AdmissionOperations.Advance(services, unit.Operation, AuthenticationOperationState.AwaitingMfa)
                : CompleteAdministratorConfirmation(services, unit));
        }, ct);
    });

    internal static Task<AuthOutcome> ProveAdministratorMfaAsync(IdentityAdmissionServices services,
        string authorization, AdministratorMfaProofRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var session = ReadSignedIn(services, authorization);
        if (session is null) return Refuse(AuthenticationFailure.InvalidGrant);
        var reference = await AdmissionOperations.ReadAsync(services, request.Handle, AuthenticationOperationPurpose.AdministratorConfirmation, ct);
        if (reference is null || reference.UserID != session.Proof.UserID) return Refuse(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
        {
            var failure = CheckConfirmation(services, unit, session, authorization, reference, request.CodeVerifier, AuthenticationOperationState.AwaitingMfa);
            if (failure is not null) return Task.FromResult<AuthOutcome>(failure);
            if (unit.Operation!.PasswordProvenAt is null || ConfirmationStep(services, unit, false) != AuthenticationStep.ExistingMfa)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
            failure = AdmissionOperations.VerifyMfa(services, unit, request.Code);
            return Task.FromResult<AuthOutcome>(failure ?? CompleteAdministratorConfirmation(services, unit));
        }, ct);
    });

    // The session may owe nothing. An account held to a trusted tenant owes what its Microsoft sessions owe.
    private static AuthenticationRefused? ConfirmationSessionRefusal(IdentityAdmissionServices services,
        IdentitySecurityTransaction unit, SignedInContext session) =>
        SignedInRefusal(services, unit, session) ?? ((RequiredTenant(services, unit.User) is { } tenant
            ? SessionStep(services, unit, SignInProvider.Microsoft, true, tenant.ID) : LocalStep(unit, true)) is not null
            ? Refuse(AuthenticationFailure.InvalidProof) : null);

    private static AuthenticationRefused? CheckConfirmation(IdentityAdmissionServices services, IdentitySecurityTransaction unit,
        SignedInContext session, string authorization, AdmissionOperations.Reference reference, string verifier, params AuthenticationOperationState[] states)
    {
        if (session.Proof.UserID != reference.UserID || session.Proof.External || unit.Operation?.SourceSessionDigest is not { } digest ||
            !CryptographicOperations.FixedTimeEquals(digest, SessionDigest(authorization))) return Refuse(AuthenticationFailure.InvalidGrant);
        return ConfirmationSessionRefusal(services, unit, session) ?? AdmissionOperations.Check(services, unit, reference, verifier,
            AuthenticationOperationPurpose.AdministratorConfirmation, states);
    }

    private static AuthOutcome CompleteAdministratorConfirmation(IdentityAdmissionServices services, IdentitySecurityTransaction unit)
    {
        var operation = unit.Operation!;
        var now = services.Clock.GetUtcNow();
        // A provider confirmation marks its session, so the operator's protected changes accept it as recent proof.
        var proof = Proof(unit, services, operation.MfaProvenAt is not null, operation.PasswordProvenAt!.Value) with
        { ProviderConfirmed = operation.SessionProvider is not null };
        AdmissionOperations.Finish(operation, now);
        unit.Audit("AdministratorConfirmed", now, operation.ID);
        // Only actual password and applicable MFA proof creates this authentication time. Renewal keeps it.
        return Issue(services, unit, proof, now);
    }

    private static byte[] SessionDigest(string authorization) => SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..]));
}
