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
    }

    private static IResult Result(AuthOutcome result) => Results.Json<AuthOutcome>(result, statusCode: result switch
    {
        AuthenticationRefused { Code: AuthenticationFailure.Unavailable } => 503,
        AuthenticationRefused { Code: AuthenticationFailure.StaleOperation } => 409,
        AuthenticationRefused => 400,
        _ => 200
    });
}
