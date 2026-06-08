using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using ShiftSoftware.ShiftEntity.Core.Extensions;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Helpers;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Endpoints;

internal static class CookieAuthEndpoints
{
    internal static async Task<IResult> Me(HttpContext httpContext)
    {
        // By the time this authorized endpoint runs, the cookie middleware's OnValidatePrincipal
        // has already refreshed any near-expiry JWT, so HttpContext.User holds the freshest claims
        // this request can produce — we just project them into the response.
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

    internal static async Task<IResult> Logout(HttpContext httpContext, IAntiforgery antiforgery)
    {
        // Invoked exclusively via HTML form post (see UserAvatar, ChangePasswordForm), which
        // posts <AntiforgeryToken />. Validate it so a cross-origin POST can't force-logout the
        // user — defense-in-depth on top of SameSite=Lax.
        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest();
        }

        string? returnUrl = null;
        if (httpContext.Request.HasFormContentType)
        {
            var form = await httpContext.Request.ReadFormAsync();
            returnUrl = form["returnUrl"];
        }

        var safeReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) && returnUrl.IsLocalUrl()
            ? returnUrl : "/";

        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = safeReturnUrl },
            new[] { CookieAuthenticationDefaults.AuthenticationScheme });
    }
}
