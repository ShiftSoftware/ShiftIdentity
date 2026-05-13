using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Helpers;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Endpoints;

internal static class CookieAuthEndpoints
{
    internal static async Task<IResult> Me(HttpContext httpContext)
    {
        // [Authorize] gates access. By the time we get here, the cookie middleware has run
        // OnValidatePrincipal — which means a near-expiry JWT will already have been refreshed
        // (with fresh claims from the identity server) and the new cookie issued. So
        // HttpContext.User holds the freshest claims this request can produce, and we just
        // project them into the response.
        var authResult = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        var refreshAfter = 60;
        var expiresAtStr = authResult.Properties?.GetString("token_expires_at");
        if (expiresAtStr != null && DateTimeOffset.TryParse(expiresAtStr, out var expiresAt))
            refreshAfter = CookieAuthHelpers.ComputeRefreshAfterSecondsFromExpiresAt(expiresAt);

        return Results.Ok(new CookieAuthStateResponse
        {
            Claims = CookieAuthHelpers.ProjectClaims(httpContext.User.Claims),
            RefreshAfterSeconds = refreshAfter,
        });
    }

    internal static async Task<IResult> Logout(HttpContext httpContext)
    {
        // Invoked exclusively via HTML form post (see UserAvatar). The form supplies a
        // returnUrl so the browser lands on a sensible page after the cookie is cleared.
        string? returnUrl = null;
        if (httpContext.Request.HasFormContentType)
        {
            var form = await httpContext.Request.ReadFormAsync();
            returnUrl = form["returnUrl"];
        }

        var safeReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
            ? returnUrl
            : "/";

        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = safeReturnUrl },
            new[] { CookieAuthenticationDefaults.AuthenticationScheme });
    }
}
