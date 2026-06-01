using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
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

        var tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.TokenLifeTimeInSeconds ?? 900);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            AllowRefresh = true,
        };

        // Challenge tokens (no refresh token) must expire with the JWT so the session
        // doesn't linger. Normal tokens leave ExpiresUtc unset so the cookie middleware's
        // ExpireTimeSpan + SlidingExpiration govern lifetime, and OnValidatePrincipal can
        // refresh the JWT even if the user returns after hours of inactivity.
        if (string.IsNullOrEmpty(token.RefreshToken))
        {
            authProperties.ExpiresUtc = tokenExpiresAt;
        }
        authProperties.SetString("refresh_token", token.RefreshToken ?? string.Empty);
        // The raw JWT is needed by ExternalCookieChangePasswordHandler when completing a
        // challenge — there's no refresh token to swap, so the cookie must carry the
        // challenge JWT directly for the Bearer call to /Auth/CompletePasswordChange.
        authProperties.SetString("access_token", token.Token);
        authProperties.SetString("token_expires_at", tokenExpiresAt.ToString("o"));

        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);
        return claims;
    }

    internal static List<Claim> BuildClaimsFromToken(TokenDTO token)
    {
        // Modern JsonWebTokenHandler matches what the client uses in JwtRefreshStrategy —
        // one JWT parser project-wide. Both ReadJwtToken and ReadToken(...) return claims
        // with their raw JWT names (no inbound mapping); the consolidation is for hygiene
        // and to avoid the legacy handler's static InboundClaimTypeMap foot-gun if anyone
        // later swaps ReadJwtToken for ValidateToken.
        var jwt = new JsonWebTokenHandler().ReadToken(token.Token) as JsonWebToken
            ?? throw new InvalidOperationException("Token is not a JWT.");

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
    /// Computes how many seconds the client should wait before polling again.
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
