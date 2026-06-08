using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Models;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Helpers;

/// <summary>
/// Validates JWTs received from an external identity server before their claims are bound to
/// the local cookie, so a host returning a TokenDTO-shaped body can't mint arbitrary claims.
/// Pinned to RS256 to close algorithm-confusion attacks.
/// </summary>
internal sealed class ExternalJwtValidator
{
    private readonly TokenValidationParameters _parameters;
    private readonly JwtSecurityTokenHandler _handler = new();

    public ExternalJwtValidator(ShiftIdentityCookieAuthOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.JwtIssuer))
            throw new InvalidOperationException(
                $"{nameof(ShiftIdentityCookieAuthOptions.JwtIssuer)} must be set for external identity hosting.");
        if (string.IsNullOrWhiteSpace(options.JwtPublicKey))
            throw new InvalidOperationException(
                $"{nameof(ShiftIdentityCookieAuthOptions.JwtPublicKey)} must be set for external identity hosting.");

        var rsa = RSA.Create();
        rsa.ImportRSAPublicKey(Convert.FromBase64String(options.JwtPublicKey), out _);

        _parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new RsaSecurityKey(rsa),
            ValidateIssuer = true,
            ValidIssuer = options.JwtIssuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
            ClockSkew = TimeSpan.Zero,
        };

        // The legacy handler default-maps JWT claim types (sub → nameidentifier, etc.). The
        // rest of the codebase reads claims by their original short names, so disable the map.
        _handler.InboundClaimTypeMap.Clear();
    }

    /// <summary>
    /// Throws <see cref="SecurityTokenException"/> (or subtype) if the token is invalid.
    /// Returns silently on success.
    /// </summary>
    public void Validate(string token)
    {
        if (string.IsNullOrEmpty(token))
            throw new SecurityTokenException("Token is empty.");

        _handler.ValidateToken(token, _parameters, out _);
    }
}
