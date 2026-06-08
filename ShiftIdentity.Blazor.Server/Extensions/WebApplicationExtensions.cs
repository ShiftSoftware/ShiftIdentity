using Microsoft.AspNetCore.Builder;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Endpoints;
using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Extensions;

public static class WebApplicationExtensions
{
    /// <summary>
    /// Maps the cookie auth endpoints: <c>GET /api/identity/me</c> (authenticated, used by the
    /// WASM refresh poll) and <c>POST /api/identity/logout</c> (called via HTML form post per
    /// the form-post contract).
    /// </summary>
    public static WebApplication MapShiftIdentityCookieEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/identity")
            .RequireRateLimiting(Constants.DefaultPolicyName);

        group.MapPost("/logout", (Delegate)CookieAuthEndpoints.Logout).AllowAnonymous();
        group.MapGet("/me", (Delegate)CookieAuthEndpoints.Me).RequireAuthorization();

        return app;
    }
}
