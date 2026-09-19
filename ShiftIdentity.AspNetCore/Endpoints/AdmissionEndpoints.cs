using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core.Authentication;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Endpoints;

/// <summary>
/// The authority's services and its api/identity/v2 routes. A host reaches them through AddShiftIdentityAuthority and
/// MapShiftIdentityAuthority (called by the dashboard registration when the authority is enabled); the fixtures call them directly.
/// </summary>
internal static class AdmissionEndpoints
{
    internal static void AddIdentityAdmissionAuthentication(this IServiceCollection services)
    {
        services.AddSingleton(sp => new IdentityMaterialProtector(
            sp.GetRequiredService<Core.ShiftIdentityConfiguration>().FactorProtection));
        services.AddHostedService<LegacyTotpMigration>();
        services.AddHostedService<AdmissionMaintenance>();
        // The legacy administrator writers find the staged authority through this registration; without it they
        // keep their direct writes. The operator is read from the current request.
        services.AddHttpContextAccessor();
        services.AddScoped<Data.Services.IUserAccountAuthority, AdmissionUserAccountAuthority>();
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, OperationAuthenticationHandler>(
            OperationAuthenticationHandler.SchemeName, _ => { });
        services.AddAuthorizationBuilder().AddPolicy("IdentityLoginContinuation", policy =>
            policy.AddAuthenticationSchemes(OperationAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim(OperationAuthenticationHandler.PurposeClaim, AuthenticationOperationPurpose.Login.ToString()));
        services.AddAuthorizationBuilder().AddPolicy("IdentityPasswordChange", policy =>
            policy.AddAuthenticationSchemes(OperationAuthenticationHandler.SchemeName).RequireAuthenticatedUser()
                .RequireClaim(OperationAuthenticationHandler.PurposeClaim, AuthenticationOperationPurpose.PasswordChange.ToString()));
        services.AddAuthorizationBuilder().AddPolicy("IdentityOperation", policy =>
            policy.AddAuthenticationSchemes(OperationAuthenticationHandler.SchemeName).RequireAuthenticatedUser()
                .RequireClaim(OperationAuthenticationHandler.PurposeClaim,
                    AuthenticationOperationPurpose.Login.ToString(), AuthenticationOperationPurpose.PasswordChange.ToString(),
                    AuthenticationOperationPurpose.MfaEnrollment.ToString(), AuthenticationOperationPurpose.MfaReplacement.ToString(), AuthenticationOperationPurpose.MfaRecovery.ToString(),
                    AuthenticationOperationPurpose.AdministratorConfirmation.ToString()));
        services.AddAuthorizationBuilder().AddPolicy("IdentityMfaEnrollment", policy =>
            policy.AddAuthenticationSchemes(OperationAuthenticationHandler.SchemeName).RequireAuthenticatedUser()
                .RequireClaim(OperationAuthenticationHandler.PurposeClaim, AuthenticationOperationPurpose.MfaEnrollment.ToString()));
        services.AddAuthorizationBuilder().AddPolicy("IdentityMfaReplacement", policy =>
            policy.AddAuthenticationSchemes(OperationAuthenticationHandler.SchemeName).RequireAuthenticatedUser()
                .RequireClaim(OperationAuthenticationHandler.PurposeClaim, AuthenticationOperationPurpose.MfaReplacement.ToString()));
        services.AddAuthorizationBuilder().AddPolicy("IdentityNewFactor", policy =>
            policy.AddAuthenticationSchemes(OperationAuthenticationHandler.SchemeName).RequireAuthenticatedUser()
                .RequireClaim(OperationAuthenticationHandler.PurposeClaim, AuthenticationOperationPurpose.MfaEnrollment.ToString(),
                    AuthenticationOperationPurpose.PasswordChange.ToString(), AuthenticationOperationPurpose.MfaReplacement.ToString(), AuthenticationOperationPurpose.MfaRecovery.ToString()));
    }

    internal static void MapIdentityAdmissionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/identity/v2").AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.HttpContext.Response.Headers["Referrer-Policy"] = "no-referrer";
            if (context.HttpContext.Request.Path.Value is "/api/identity/v2/security-link/open" or "/api/identity/v2/password-reset/complete" or "/api/identity/v2/email-verification/complete")
            {
                var admission = context.HttpContext.RequestServices.GetRequiredService<IdentityAdmissionServices>();
                var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var key = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(admission.Options.OperationKey,
                    System.Text.Encoding.UTF8.GetBytes("link:" + ip)));
                try
                {
                    if (!await admission.Store.ConsumeIngressAsync(key, admission.Clock.GetUtcNow(), admission.DeliveryLimits.LinkRequestsPerIpPer15Minutes,
                        TimeSpan.FromMinutes(15), context.HttpContext.RequestAborted))
                        return Result(new AuthenticationRefused(AuthenticationFailure.AttemptsExhausted));
                }
                catch (ShiftSoftware.ShiftIdentity.Data.Authentication.IdentitySecurityUnavailableException)
                { return Result(new AuthenticationRefused(AuthenticationFailure.Unavailable)); }
            }
            return await next(context);
        });
        group.MapPost("/login", async (PasswordLoginRequest request, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AuthService.BeginLoginAsync(services, request, ct)));
        group.MapPost("/login/mfa", async (CompleteMfaRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AuthService.CompleteLoginMfaAsync(services, context.Request.Headers.Authorization.ToString()[10..], request, ct)))
            .RequireAuthorization("IdentityLoginContinuation");
        group.MapPost("/refresh", async (RenewSessionRequest request, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AuthService.RenewSessionAsync(services, request, ct)));
        group.MapPost("/admin-confirmation", async (StartPasswordChangeRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.BeginAdministratorConfirmationAsync(services, context.Request.Headers.Authorization.ToString(), request, ct)));
        group.MapPost("/admin-confirmation/password", async (AdministratorPasswordProofRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.ProveAdministratorPasswordAsync(services, context.Request.Headers.Authorization.ToString(), request, ct)));
        group.MapPost("/admin-confirmation/mfa", async (AdministratorMfaProofRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.ProveAdministratorMfaAsync(services, context.Request.Headers.Authorization.ToString(), request, ct)));
        group.MapPost("/password-change", async (StartPasswordChangeRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.BeginPasswordChangeAsync(services, context.Request.Headers.Authorization.ToString(), request, ct)));
        group.MapPost("/password-change/password", async (PasswordChangeProofRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.ProvePasswordAsync(services, context.Request.Headers.Authorization.ToString()[10..], request, ct)))
            .RequireAuthorization("IdentityPasswordChange");
        group.MapPost("/password-change/complete", async (CompletePasswordChangeRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.CompletePasswordChangeAsync(services, context.Request.Headers.Authorization.ToString()[10..], request, ct)))
            .RequireAuthorization("IdentityPasswordChange");
        group.MapPost("/password-change/mfa", async (CompleteMfaRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.CompleteMfaAsync(services, context.Request.Headers.Authorization.ToString()[10..], request, ct)))
            .RequireAuthorization("IdentityPasswordChange");
        group.MapPost("/operations/cancel", async (CancelOperationRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.CancelAsync(services, context.Request.Headers.Authorization.ToString()[10..], request, ct)))
            .RequireAuthorization("IdentityOperation");
        group.MapGet("/mfa", async (HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.ReadAuthenticatorAsync(services, context.Request.Headers.Authorization.ToString(), ct)));
        group.MapPost("/mfa/start", async (StartMfaRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.BeginMfaAsync(services, context.Request.Headers.Authorization.ToString(), request, ct)));
        group.MapPost("/mfa/password", async (PasswordChangeProofRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.ProveMfaPasswordAsync(services, context.Request.Headers.Authorization.ToString()[10..], request, ct)))
            .RequireAuthorization("IdentityMfaEnrollment");
        group.MapPost("/mfa/existing", async (CompleteMfaRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.ProveExistingFactorAsync(services, context.Request.Headers.Authorization.ToString()[10..], request, ct)))
            .RequireAuthorization("IdentityMfaReplacement");
        group.MapPost("/mfa/confirm", async (CompleteMfaRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.ConfirmNewFactorAsync(services, context.Request.Headers.Authorization.ToString()[10..], request, ct)))
            .RequireAuthorization("IdentityNewFactor");
        group.MapPost("/mfa/recover", async (RecoverMfaRequest request, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.RecoverMfaAsync(services, request, ct)));
        group.MapPost("/mfa/recovery-code", async (IssueMfaRecoveryRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.IssueMfaRecoveryAsync(services, context.Request.Headers.Authorization.ToString(), request, ct)));
        group.MapPost("/password-reset/request", async (RequestSecurityEmail request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.RequestSecurityEmailAsync(services, request, false, context.Connection.RemoteIpAddress?.ToString() ?? "unknown", ct)));
        group.MapPost("/email-verification/request", async (RequestSecurityEmail request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.RequestSecurityEmailAsync(services, request, true, context.Connection.RemoteIpAddress?.ToString() ?? "unknown", ct)));
        group.MapPost("/email-verification/request-current", async (HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.RequestCurrentEmailVerificationAsync(services, context.Request.Headers.Authorization.ToString(), ct)));
        group.MapPost("/password-reset/admin", async (AdminPasswordResetRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.AdminSecurityLinkAsync(services, context.Request.Headers.Authorization.ToString(), LinkTarget(services, request.UserID, request.UserKey),
                request.Manual ? AuthenticationOperationPurpose.PasswordResetManual : AuthenticationOperationPurpose.PasswordResetEmail, ct)));
        group.MapPost("/email-verification/admin", async (AdminEmailVerificationRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.AdminSecurityLinkAsync(services, context.Request.Headers.Authorization.ToString(), LinkTarget(services, request.UserID, request.UserKey), AuthenticationOperationPurpose.EmailVerify, ct)));
        group.MapPost("/admin/password", async (AdminSetPasswordRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.SetPasswordAsync(services, context.Request.Headers.Authorization.ToString(), request, ct)));
        group.MapPost("/admin/username", async (AdminUsernameChangeRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.ChangeUsernameAsync(services, context.Request.Headers.Authorization.ToString(), request, ct)));
        group.MapPost("/admin/email", async (AdminEmailChangeRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.ChangeEmailAsync(services, context.Request.Headers.Authorization.ToString(), request, ct)));
        group.MapPost("/admin/status", async (AdminAccountStatusRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.SetActiveAsync(services, context.Request.Headers.Authorization.ToString(), request, ct)));
        group.MapPost("/security-link/open", async (OpenSecurityLinkRequest request, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.OpenSecurityLinkAsync(services, request, ct)));
        group.MapPost("/password-reset/complete", async (CompletePasswordResetRequest request, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.CompletePasswordResetAsync(services, request, ct)));
        group.MapPost("/email-verification/complete", async (CompleteEmailVerificationRequest request, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AccountSecurityService.CompleteEmailVerificationAsync(services, request, ct)));
    }

    private static long LinkTarget(IdentityAdmissionServices services, long id, string? key)
    {
        if (key is null) return id;
        if (id != 0 || key.Length is 0 or > 255) return 0;
        try { return services.HashIds.Decode<Core.DTOs.User.UserDTO>(key); }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException) { return 0; }
    }

    private static IResult Result(AuthOutcome result) => Results.Json<AuthOutcome>(result, statusCode: result switch
    {
        AuthenticationRefused { Code: AuthenticationFailure.Unavailable } => 503,
        AuthenticationRefused { Code: AuthenticationFailure.StaleOperation } => 409,
        AuthenticationRefused { Code: AuthenticationFailure.ReauthenticationRequired } => 403,
        AuthenticationRefused => 400,
        SecurityDeliveryRequested => 202,
        _ => 200
    });
}
