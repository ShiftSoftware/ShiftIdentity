using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core.Authentication;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Endpoints;

/// <summary>Internal staged wiring, used only by isolated automated/consumer fixtures in this slice.</summary>
internal static class AdmissionEndpoints
{
    internal static void AddIdentityAdmissionAuthentication(this IServiceCollection services)
    {
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
                    AuthenticationOperationPurpose.Login.ToString(), AuthenticationOperationPurpose.PasswordChange.ToString()));
    }

    internal static void MapIdentityAdmissionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/identity/v2").AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });
        group.MapPost("/login", async (PasswordLoginRequest request, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AuthService.BeginLoginAsync(services, request, ct)));
        group.MapPost("/login/mfa", async (CompleteMfaRequest request, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AuthService.CompleteLoginMfaAsync(services, context.Request.Headers.Authorization.ToString()[10..], request, ct)))
            .RequireAuthorization("IdentityLoginContinuation");
        group.MapPost("/refresh", async (RenewSessionRequest request, IdentityAdmissionServices services, CancellationToken ct) =>
            Result(await AuthService.RenewSessionAsync(services, request, ct)));
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
    }

    private static IResult Result(AuthOutcome result) => Results.Json<AuthOutcome>(result, statusCode: result switch
    {
        AuthenticationRefused { Code: AuthenticationFailure.Unavailable } => 503,
        AuthenticationRefused { Code: AuthenticationFailure.StaleOperation } => 409,
        AuthenticationRefused => 400,
        _ => 200
    });
}
