using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using static ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.AdmissionRules;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Endpoints;

/// <summary>Adapters for the deployed login's existing restricted-step routes. No registration activates authority.</summary>
public static class LegacyLoginEndpoints
{
    public static bool IsStaged(HttpContext http) => http.RequestServices.GetService<IdentityAdmissionServices>() is not null;

    internal static IResult TokenResult(AuthOutcome outcome, string message)
    {
        if (AuthService.CompatibleLoginToken(outcome) is { } token) return Results.Ok(new ShiftEntityResponse<TokenDTO>(token));
        message = outcome switch
        {
            ChallengeRequired { Challenge.Step: AuthenticationStep.EmailVerification } => "Verify your saved email address before signing in.",
            ChallengeRequired { Challenge.Step: AuthenticationStep.MfaRecovery } => "Authenticator recovery is required. Contact your administrator.",
            _ => message
        };
        return Results.Json(new ShiftEntityResponse<TokenDTO> { Message = new Message { Body = message } },
            statusCode: outcome is AuthenticationRefused { Code: AuthenticationFailure.Unavailable } ? 503 : 400);
    }

    public static async Task<IResult> CompleteMfaAsync(HttpContext http, string code)
    {
        var services = Services(http);
        var authorization = http.Request.Headers.Authorization.ToString();
        var credential = new LegacyLoginTokenCodec(services).Read(authorization);
        AuthOutcome outcome = Refuse(AuthenticationFailure.InvalidGrant);
        if (credential is { Flow: AuthPurpose.Mfa })
        {
            var request = new CompleteMfaRequest(code, credential.Verifier);
            outcome = credential.Purpose == AuthenticationOperationPurpose.Login
                ? await AuthService.CompleteLoginMfaAsync(services, credential.Handle, request, http.RequestAborted)
                : await AccountSecurityService.CompleteMfaAsync(services, credential.Handle, request, http.RequestAborted);
            outcome = await AuthService.CompatibleLoginOutcomeAsync(services, outcome, credential.Verifier, http.RequestAborted);
        }
        else if (credential is null && LegacyTemporary(services, authorization) is (var token, { Purpose: AuthPurpose.Mfa } legacy))
        {
            // The old issuer's MFA step, finished through the staged bridge with a real current factor proof.
            outcome = await AuthService.CompleteLegacyMfaAsync(services, token, legacy, code, http.RequestAborted);
            if (outcome is ChallengeRequired { Challenge.Step: AuthenticationStep.PasswordChange or AuthenticationStep.NewMfa })
                return TokenResult(outcome, "Sign in again to continue.");
        }
        return TokenResult(outcome, "Invalid code");
    }

    /// <summary>The deployed pre-cutover step credential, while the staged host still carries the legacy temporary settings.</summary>
    private static (string Token, LegacyTemporaryTokenCodec.Proof Proof)? LegacyTemporary(IdentityAdmissionServices services, string authorization)
    {
        if (services.LegacyTemporaryTokens is not { } codec ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = authorization[7..].Trim();
        return codec.Validate(token) is { } proof ? (token, proof) : null;
    }

    public static async Task<IResult> ChangePasswordAsync(HttpContext http, ChangePasswordDTO dto)
    {
        var services = Services(http);
        var credential = new LegacyLoginTokenCodec(services).Read(http.Request.Headers.Authorization);
        AuthOutcome outcome = Refuse(AuthenticationFailure.InvalidGrant);
        if (credential is { Flow: AuthPurpose.ChangePassword, Purpose: AuthenticationOperationPurpose.PasswordChange } &&
            dto.NewPassword == dto.ConfirmPassword && dto.CurrentPassword is { Length: > 0 and <= 255 })
        {
            outcome = await AccountSecurityService.CompletePasswordChangeAsync(services, credential.Handle,
                new(dto.NewPassword, credential.Verifier), http.RequestAborted, dto.CurrentPassword);
            outcome = await AuthService.CompatibleLoginOutcomeAsync(services, outcome, credential.Verifier, http.RequestAborted);
        }
        return TokenResult(outcome, "Could not change password. Check the current password and new password requirements.");
    }

    public static async Task<IResult> StartEnrollmentAsync(HttpContext http)
    {
        var services = Services(http);
        var credential = new LegacyLoginTokenCodec(services).Read(http.Request.Headers.Authorization);
        var outcome = await ReadSetupAsync(services, credential, http.RequestAborted);
        return outcome is SetupIssued setup ? Results.Ok(new ShiftEntityResponse<TotpDTO>(setup.Setup))
            : Results.Json(new ShiftEntityResponse<TotpDTO> { Message = new Message { Body = "Invalid token." } },
                statusCode: outcome is AuthenticationRefused { Code: AuthenticationFailure.Unavailable } ? 503 : 400);
    }

    public static async Task<IResult> ConfirmEnrollmentAsync(HttpContext http, TotpDTO dto)
    {
        var services = Services(http);
        var credential = new LegacyLoginTokenCodec(services).Read(http.Request.Headers.Authorization);
        var setup = await ReadSetupAsync(services, credential, http.RequestAborted);
        AuthOutcome outcome = setup is AuthenticationRefused ? setup : Refuse(AuthenticationFailure.InvalidGrant);
        if (setup is SetupIssued issued && dto.Secret == issued.Setup.Secret && dto.Expires == issued.Setup.Expires &&
            dto.SasToken is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(dto.SasToken), Encoding.UTF8.GetBytes(issued.Setup.SasToken)))
        {
            // The shared confirmation rechecks the operation/version/factor under the lock and verifies its stored
            // pending secret. The echoed client secret can never replace that material.
            outcome = await AccountSecurityService.ConfirmNewFactorAsync(services, credential!.Handle,
                new(dto.Code!, credential.Verifier), http.RequestAborted);
            outcome = await AuthService.CompatibleLoginOutcomeAsync(services, outcome, credential.Verifier, http.RequestAborted);
        }
        return TokenResult(outcome, "Invalid code or enrollment token.");
    }

    private sealed record SetupIssued(TotpDTO Setup) : AuthOutcome;

    private static Task<AuthOutcome> ReadSetupAsync(IdentityAdmissionServices services, LegacyLoginTokenCodec.Credential? credential,
        CancellationToken ct) => AtBoundary(async () =>
    {
        if (credential is not { Flow: AuthPurpose.MfaEnrollment }) return Refuse(AuthenticationFailure.InvalidGrant);
        var reference = await AdmissionOperations.ReadAsync(services, credential.Handle, credential.Purpose, ct);
        if (reference is null) return Refuse(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
        {
            var refusal = AdmissionOperations.Check(services, unit, reference, credential.Verifier, credential.Purpose,
                AuthenticationOperationState.AwaitingNewFactor);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            if (unit.Security.ProtectedTotpSecret is not null || unit.Security.LocalMfaRecoveryRequired || unit.Operation!.PasswordProvenAt is null)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
            var secret = MfaMaterial.ReadPending(services, unit.Operation!);
            try
            {
                var setup = MfaMaterial.Describe(unit, secret);
                var expires = unit.Operation!.ExpiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var signature = HMACSHA256.HashData(services.Options.OperationKey,
                    Encoding.UTF8.GetBytes($"LegacyEnrollment.v1:{unit.Operation.ID:N}:{setup.Secret}:{expires}"));
                return Task.FromResult<AuthOutcome>(new SetupIssued(new()
                {
                    Secret = setup.Secret, Uri = setup.Uri, Svg = setup.Svg,
                    Expires = expires, SasToken = Convert.ToBase64String(signature)
                }));
            }
            finally { CryptographicOperations.ZeroMemory(secret); }
        }, ct);
    });

    private static IdentityAdmissionServices Services(HttpContext http)
    {
        http.Response.Headers["Cache-Control"] = "no-store";
        return http.RequestServices.GetRequiredService<IdentityAdmissionServices>();
    }
}
