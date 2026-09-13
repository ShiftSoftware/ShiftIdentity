using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.ShiftIdentity.Core.Models;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>Validates only the deployed subject-and-expiry refresh credential.</summary>
internal sealed class LegacyRefreshTokenCodec(RefreshTokenSettingsModel settings, TimeProvider clock)
{
    private static readonly HashSet<string> AllowedPayloadClaims = new(StringComparer.Ordinal)
    {
        "iss", "aud", "exp", "nbf", "iat", "jti", "sub", "nameid", ClaimTypes.NameIdentifier
    };

    internal sealed record Proof(string Subject, DateTimeOffset ExpiresAt);

    internal static LegacyRefreshTokenCodec? TryCreate(RefreshTokenSettingsModel? settings, TimeProvider clock) =>
        settings is not null && !string.IsNullOrWhiteSpace(settings.Key) &&
        !string.IsNullOrWhiteSpace(settings.Issuer) && !string.IsNullOrWhiteSpace(settings.Audience) &&
        settings.ExpireSeconds > 0
            ? new(settings, clock) : null;

    internal Proof? Validate(string token)
    {
        try
        {
            if (string.IsNullOrEmpty(token) || token.Length > 16384) return null;
            var pieces = token.Split('.');
            if (pieces.Length != 3) return null;
            using var header = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(pieces[0]));
            using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(pieces[1]));
            if (header.RootElement.ValueKind != JsonValueKind.Object || payload.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            if (HasDuplicateNames(header.RootElement) || HasDuplicateNames(payload.RootElement)) return null;
            var properties = payload.RootElement.EnumerateObject().ToArray();
            if (properties.Any(x => !AllowedPayloadClaims.Contains(x.Name))) return null;
            var subjects = properties.Where(x => x.Name is "sub" or "nameid" || x.Name == ClaimTypes.NameIdentifier).ToArray();
            if (subjects.Length != 1 || subjects[0].Value.ValueKind != JsonValueKind.String ||
                subjects[0].Value.GetString() is not { Length: > 0 and <= 255 } subject || string.IsNullOrWhiteSpace(subject))
                return null;

            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 16384 };
            handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidIssuer = settings.Issuer,
                ValidAudience = settings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha512Signature],
                ClockSkew = TimeSpan.Zero,
                LifetimeValidator = (notBefore, expires, _, _) =>
                    expires is not null && expires > clock.GetUtcNow().UtcDateTime &&
                    (notBefore is null || notBefore <= clock.GetUtcNow().UtcDateTime)
            }, out var validated);
            if (validated is not JwtSecurityToken jwt ||
                !string.Equals(jwt.Header.Alg, SecurityAlgorithms.HmacSha512Signature, StringComparison.Ordinal)) return null;
            var expiresAt = new DateTimeOffset(DateTime.SpecifyKind(jwt.ValidTo, DateTimeKind.Utc));
            if (expiresAt > clock.GetUtcNow().AddSeconds(settings.ExpireSeconds)) return null;
            return new(subject, expiresAt);
        }
        catch (Exception e) when (e is SecurityTokenException or ArgumentException or FormatException or JsonException)
        {
            return null;
        }
    }

    private static bool HasDuplicateNames(JsonElement value)
    {
        var names = value.EnumerateObject().Select(x => x.Name).ToArray();
        return names.Distinct(StringComparer.Ordinal).Count() != names.Length;
    }
}
