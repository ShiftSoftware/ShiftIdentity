using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using ShiftSoftware.ShiftIdentity.Core.Models;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>
/// Validates only the deployed pre-cutover temporary step credential: the symmetric JWT the legacy issuer signs
/// with the temporary-token key after a password check, naming the user, the step purpose and an expiry. It binds
/// no security version, factor generation or operation, carries no issuance time, and is never an access token, a
/// refresh token or a v2 operation handle. The route that consumes it still requires the step's real proof.
/// </summary>
internal sealed class LegacyTemporaryTokenCodec(TemporaryTokenSettingsModel settings, TimeProvider clock)
{
    private static readonly HashSet<string> AllowedPayloadClaims = new(StringComparer.Ordinal)
    {
        "iss", "aud", "exp", "nbf", "iat", "jti", "sub", "nameid", ClaimTypes.NameIdentifier,
        "unique_name", ClaimTypes.Name, "given_name", ClaimTypes.GivenName, ShiftIdentityClaims.TokenPurpose
    };

    internal sealed record Proof(string Subject, AuthPurpose Purpose, DateTimeOffset ExpiresAt);

    internal static LegacyTemporaryTokenCodec? TryCreate(TemporaryTokenSettingsModel? settings, TimeProvider clock) =>
        settings is not null && !string.IsNullOrWhiteSpace(settings.Key) &&
        !string.IsNullOrWhiteSpace(settings.Issuer) && settings.ExpireSeconds > 0
            ? new(settings, clock) : null;

    internal Proof? Validate(string? token)
    {
        try
        {
            if (string.IsNullOrEmpty(token) || token.Length > 8192) return null;
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
            // The purpose is the exact enum name the deployed issuer writes; a number or an unknown name is not a step.
            var purposes = properties.Where(x => x.Name == ShiftIdentityClaims.TokenPurpose).ToArray();
            if (purposes.Length != 1 || purposes[0].Value.ValueKind != JsonValueKind.String ||
                purposes[0].Value.GetString() is not { } purposeText ||
                !Enum.TryParse<AuthPurpose>(purposeText, out var purpose) || purpose == AuthPurpose.None ||
                purpose.ToString() != purposeText)
                return null;

            // The deployed validator ignores the audience; when one is configured the issued credentials carry it.
            var validateAudience = !string.IsNullOrWhiteSpace(settings.Audience);
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 8192 };
            handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidIssuer = settings.Issuer,
                ValidAudience = validateAudience ? settings.Audience : null,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),
                ValidateIssuer = true,
                ValidateAudience = validateAudience,
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
            // The deployed issuer sets the expiry one configured lifetime after issuance; a later expiry was not issued by it.
            if (expiresAt > clock.GetUtcNow().AddSeconds(settings.ExpireSeconds)) return null;
            return new(subject, purpose, expiresAt);
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
