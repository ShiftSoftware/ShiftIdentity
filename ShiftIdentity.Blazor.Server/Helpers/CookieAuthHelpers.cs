using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Helpers;

internal static class CookieAuthHelpers
{
    internal static async Task SignInWithToken(HttpContext httpContext, TokenDTO token)
    {
        var claims = BuildClaimsFromToken(token);
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            AllowRefresh = true,
        };
        authProperties.SetString("refresh_token", token.RefreshToken);
        authProperties.SetString("token_expires_at",
            DateTimeOffset.UtcNow.AddSeconds(token.TokenLifeTimeInSeconds ?? 900).ToString("o"));

        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);
    }

    internal static List<Claim> BuildClaimsFromToken(TokenDTO token)
    {
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token.Token);

        var claims = new List<Claim>();

        foreach (var claim in jwt.Claims)
        {
            // these are some of the standard JWT claims that we don't need to include
            if (claim.Type is "nbf" or "exp" or "iat" or "iss" or "aud" or "jti")
                continue;

            claims.Add(new Claim(claim.Type, claim.Value));
        }

        return claims;
    }
}
