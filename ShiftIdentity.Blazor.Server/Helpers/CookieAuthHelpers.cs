using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Helpers;

internal static class CookieAuthHelpers
{
    // Aligned with OnValidatePrincipal's 30-second refresh window in
    // ServiceCollectionExtensions; the next poll should land inside it.
    private const int RefreshBufferSeconds = 15;

    internal static async Task<List<Claim>> SignInWithToken(HttpContext httpContext, TokenDTO token)
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
        return claims;
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

    internal static List<UserClaimModel> ProjectClaims(IEnumerable<Claim> claims)
    {
        return claims.Select(c => new UserClaimModel { Type = c.Type, Value = c.Value }).ToList();
    }

    /// <summary>
    /// Computes how many seconds the client should wait before polling again, given the
    /// JWT lifetime returned by the identity server. The result lands the next poll inside
    /// OnValidatePrincipal's refresh window so the cookie gets refreshed at that point.
    /// </summary>
    internal static int ComputeRefreshAfterSecondsFromLifetime(long? tokenLifeTimeInSeconds)
    {
        var lifetime = (int)(tokenLifeTimeInSeconds ?? 900);
        return Math.Max(RefreshBufferSeconds, lifetime - RefreshBufferSeconds);
    }

    /// <summary>
    /// Computes refreshAfterSeconds from an absolute expiry timestamp stored in the cookie's
    /// AuthenticationProperties. Used by /me where we don't have a fresh TokenDTO.
    /// </summary>
    internal static int ComputeRefreshAfterSecondsFromExpiresAt(DateTimeOffset expiresAt)
    {
        var remaining = (int)(expiresAt - DateTimeOffset.UtcNow).TotalSeconds;
        return Math.Max(RefreshBufferSeconds, remaining - RefreshBufferSeconds);
    }
}
