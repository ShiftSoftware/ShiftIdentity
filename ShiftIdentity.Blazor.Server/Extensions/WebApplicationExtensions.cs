using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.Blazor;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Endpoints;
using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Extensions;

public static class WebApplicationExtensions
{
    /// <summary>
    /// Maps the cookie auth endpoints: /api/identity/login (internal only),
    /// /api/identity/sign-in-with-token, /api/identity/refresh, /api/identity/logout.
    /// </summary>
    public static WebApplication MapShiftIdentityCookieEndpoints(this WebApplication app)
    {
        var blazorOptions = app.Services.GetRequiredService<ShiftIdentityBlazorOptions>();

        var group = app.MapGroup("/api/identity")
            .RequireRateLimiting(Constants.DefaultPolicyName);

        var anonymous = group.MapGroup("").AllowAnonymous();

        if (blazorOptions.HostingType == ShiftIdentityHostingTypes.Internal)
            anonymous.MapPost("/login", CookieAuthEndpoints.Login);

        anonymous.MapPost("/sign-in-with-token", CookieAuthEndpoints.SignInWithToken);
        anonymous.MapPost("/logout", (Delegate)CookieAuthEndpoints.Logout);

        group.MapPost("/refresh", CookieAuthEndpoints.Refresh);

        return app;
    }
}
