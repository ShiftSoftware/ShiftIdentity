using System.Security.Cryptography;
using System.Text.Json;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Auth;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using static ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.AdmissionRules;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

// This result is translated to the deployed AuthCodeModel envelope, never sent as a v2 wire outcome.
internal sealed record AppCodeIssued(AuthCodeModel Code) : AuthOutcome;

public partial class AuthService
{
    internal static Task<AuthOutcome> CreateAppCodeAsync(IdentityAdmissionServices services, string? authorization,
        GenerateAuthCodeDTO request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (request is null || !ValidAppID(request.AppId) || !ValidAppChallenge(request.CodeChallenge) ||
            !RelativeReturnUrl(request.ReturnUrl)) return Refuse(AuthenticationFailure.InvalidRequest);
        var signedIn = ReadSignedIn(services, authorization);
        // Only the identity application's ordinary session may transfer its proof to a destination app.
        if (signedIn is null || signedIn.Proof.External) return Refuse(AuthenticationFailure.InvalidGrant);
        var destination = new AuthenticationClient(request.AppId, request.AppId, External: true);
        services.Observe?.Invoke("AppCodeProof");
        return await services.Store.AdmitAppAsync<AuthOutcome>(signedIn.Proof.UserID, services.Client, destination, unit =>
        {
            var refusal = SignedInRefusal(services, unit, signedIn);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            if (!PublicApp(unit.App, destination)) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.ClientDenied));
            var now = services.Clock.GetUtcNow();
            var next = ExistingSessionStep(unit, signedIn.Proof, now);
            if (next is not null) return Task.FromResult<AuthOutcome>(Restricted(next.Value, now));
            var expiresAt = now.AddMinutes(5);
            if (signedIn.Proof.LegacyCompatibilityExpiresAt is { } compatibilityDeadline && compatibilityDeadline < expiresAt)
                expiresAt = compatibilityDeadline;
            var op = new AuthenticationOperation
            {
                ID = Guid.NewGuid(), UserID = unit.User.ID, Purpose = AuthenticationOperationPurpose.AppExchange,
                State = AuthenticationOperationState.AwaitingAppExchange,
                SecurityVersion = signedIn.Proof.SecurityVersion, PolicyRevision = signedIn.Proof.PolicyRevision,
                FactorGeneration = signedIn.Proof.FactorGeneration, ClientID = destination.ID,
                Audience = destination.Audience, External = true, CodeChallenge = request.CodeChallenge,
                AppBinding = BindApp(unit.App!), SessionAuthenticatedAt = signedIn.Proof.AuthenticatedAt,
                SessionMfaSatisfied = signedIn.Proof.MfaSatisfied,
                SessionLegacyCompatibilityExpiresAt = signedIn.Proof.LegacyCompatibilityExpiresAt,
                CreatedAt = now, ExpiresAt = expiresAt
            };
            unit.AddOperation(op);
            unit.Audit("AppCodeCreated", now, op.ID);
            return Task.FromResult<AuthOutcome>(new AppCodeIssued(new()
            {
                Code = op.ID, UserID = op.UserID, AppId = op.ClientID, AppDisplayName = unit.App!.DisplayName,
                RedirectUri = unit.App.RedirectUri, ReturnUrl = request.ReturnUrl!, Expire = op.ExpiresAt.UtcDateTime
            }));
        }, ct);
    });

    internal static Task<AuthOutcome> ExchangeAppCodeAsync(IdentityAdmissionServices services,
        GenerateExternalTokenWithAppIdOnlyDTO request, CancellationToken ct) => AtBoundary(async () =>
    {
        // Keep the deployed Guid verifier and SHA-512 challenge. Hash its exact text, including its casing.
        if (request is null || request.AuthCode == Guid.Empty || !ValidAppID(request.AppId) ||
            request.CodeVerifier is not { Length: > 0 and <= 128 } || !Guid.TryParse(request.CodeVerifier, out _))
            return Refuse(AuthenticationFailure.InvalidRequest);
        var reference = await services.Store.ReadOperationAsync(request.AuthCode, ct);
        if (reference is null || reference.Purpose != AuthenticationOperationPurpose.AppExchange ||
            reference.ClientID != request.AppId || reference.Audience != request.AppId || !reference.External)
            return Refuse(AuthenticationFailure.InvalidGrant);
        var destination = new AuthenticationClient(reference.ClientID, reference.Audience, true);
        var targetServices = services with { Client = destination };
        services.Observe?.Invoke("AppExchangeProof");
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, destination, unit =>
        {
            var op = unit.Operation;
            if (op is null || op.UserID != reference.UserID || op.Purpose != AuthenticationOperationPurpose.AppExchange ||
                op.ClientID != destination.ID || op.Audience != destination.Audience || !op.External ||
                op.State != AuthenticationOperationState.AwaitingAppExchange)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidGrant));
            var refusal = CommonRefusal(targetServices, unit, op.SecurityVersion, op.PolicyRevision);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            var now = services.Clock.GetUtcNow();
            if (now >= op.ExpiresAt) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.Expired));
            if (op.FactorGeneration != unit.Security.FactorGeneration || !PublicApp(unit.App, destination) ||
                op.AppBinding != BindApp(unit.App!) || op.SessionAuthenticatedAt is not { } authenticatedAt ||
                authenticatedAt > now || op.SessionMfaSatisfied is not { } mfa ||
                op.SessionLegacyCompatibilityExpiresAt is { } compatibilityDeadline && compatibilityDeadline <= now)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
            if (!ValidAppChallenge(op.CodeChallenge) || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(op.CodeChallenge), Convert.FromHexString(HashService.SHA512GenerateHash(request.CodeVerifier))))
            {
                op.FailedAttempts++;
                if (op.FailedAttempts >= 5)
                {
                    ClearAppCode(op, now);
                    op.State = AuthenticationOperationState.Locked;
                }
                unit.Audit("InvalidAppVerifier", now, op.ID);
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidProof));
            }
            var next = LocalStep(unit, mfa);
            var compatibility = op.SessionLegacyCompatibilityExpiresAt;
            if (compatibility is null && next is not null) return Task.FromResult<AuthOutcome>(Restricted(next.Value, now));
            var proof = new SessionProof(op.UserID, op.SecurityVersion, op.PolicyRevision, op.FactorGeneration,
                mfa, authenticatedAt, destination.ID, destination.Audience, true,
                services.HashIds.Encode<UserDTO>(unit.User.ID), op.AppBinding, compatibility);
            ClearAppCode(op, now);
            unit.Audit("AppCodeExchanged", now, op.ID);
            // Transferring an existing session does not establish fresh authentication or clear proof failures.
            return Task.FromResult(Issue(targetServices, unit, proof, now, freshAuthentication: false));
        }, ct);
    });

    internal static Task<AuthOutcome> RenewCompatibleSessionAsync(IdentityAdmissionServices services,
        RenewSessionRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var proof = services.Tokens.ValidateRefresh(request.RefreshToken);
        if (proof is null) return await ExchangeLegacyRefreshAsync(services, request, ct);
        var client = new AuthenticationClient(proof.ClientID, proof.Audience, proof.External);
        // Internal refresh remains bound to this authority; app exchanges carry their own signed destination.
        if (proof.AppBinding is null && client != services.Client) return Refuse(AuthenticationFailure.InvalidGrant);
        return await RenewSessionAsync(services with { Client = client }, request, ct, updateLastSeen: true);
    });

    internal static bool AppSessionIsCurrent(IdentitySecurityTransaction unit, SessionProof proof) =>
        proof.AppBinding is null || (PublicApp(unit.App, new(proof.ClientID, proof.Audience, proof.External)) &&
            proof.AppBinding == BindApp(unit.App!));

    private static bool PublicApp(App? app, AuthenticationClient client) =>
        app is not null && !app.IsDeleted && app.AppSecret is null && app.AppId == client.ID &&
        client.External && client.Audience == app.AppId;

    // Bind the database identity as well as the configured redirect. Reusing an AppId cannot reuse its codes.
    private static string BindApp(App app) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new { app.ID, app.AppId, app.RedirectUri })));

    private static bool ValidAppID(string? value) => value is { Length: > 0 and <= 255 } && !string.IsNullOrWhiteSpace(value);
    private static bool ValidAppChallenge(string? value) => value is { Length: 128 } && value.All(char.IsAsciiHexDigit);
    // Ask the parser for a RELATIVE Uri rather than refusing an absolute one: on Unix, Uri reads a leading '/' as an
    // absolute file path, so "/orders" would count as absolute there and every relative return URL would be refused.
    // A relative request is answered the same way on every platform, and anything carrying a scheme or an authority
    // ("https://outside.invalid", "javascript:...") is refused as CannotCreateRelative.
    private static bool RelativeReturnUrl(string? value) => value is null || (value.Length <= 4000 &&
        !value.Any(char.IsControl) && !value.Contains('\\') && !value.StartsWith("//", StringComparison.Ordinal) &&
        Uri.TryCreate(value, UriKind.Relative, out _));

    private static void ClearAppCode(AuthenticationOperation op, DateTimeOffset now)
    {
        op.State = AuthenticationOperationState.Completed; op.CompletedAt = now;
        op.CodeChallenge = ""; op.HandleDigest = []; op.AppBinding = null;
        op.SessionAuthenticatedAt = null; op.SessionMfaSatisfied = null;
        op.SessionLegacyCompatibilityExpiresAt = null;
    }
}
