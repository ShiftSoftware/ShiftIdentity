using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Data.Authentication;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>Signs only admitted immutable state; v2 refresh validation never upgrades missing context.</summary>
internal sealed class AdmissionTokenCodec(IdentityAdmissionOptions options, TimeProvider clock)
{
    internal const string Version = "shift_sv";
    internal const string Policy = "shift_policy";
    internal const string Client = "shift_client";
    internal const string Resource = "shift_resource";
    internal const string External = "shift_external";
    internal const string Purpose = "shift_purpose";
    internal const string Schema = "shift_schema";
    internal const string Factor = "shift_factor";
    internal const string Mfa = "shift_mfa";
    internal const string Route = "shift_route";
    internal const string UserID = "shift_uid";
    internal const string AppBinding = "shift_app";
    internal const string LegacyCompatibility = "shift_legacy_until";

    public TokenDTO Issue(IssuanceDecision decision)
    {
        var proof = decision.Proof;
        if (proof.UserID <= 0 || string.IsNullOrWhiteSpace(proof.Subject) || proof.SecurityVersion < 1 || proof.PolicyRevision != options.PolicyRevision ||
            options.AccessLifetimeSeconds is < 1 or > 900 || options.RefreshLifetimeSeconds < 1 ||
            options.RefreshKey.Length < 64 || options.OperationKey.Length < 32 ||
            proof.LegacyCompatibilityExpiresAt is { } compatibilityDeadline && compatibilityDeadline <= decision.AdmittedAt)
            throw new InvalidOperationException("Invalid admission configuration.");
        var now = decision.AdmittedAt;
        var common = new List<Claim>
        {
            new("sub", proof.Subject), new(UserID, proof.UserID.ToString(CultureInfo.InvariantCulture)),
            new(Schema, "2"), new(Version, proof.SecurityVersion.ToString(CultureInfo.InvariantCulture)),
            new(Policy, proof.PolicyRevision.ToString(CultureInfo.InvariantCulture)),
            new(Factor, proof.FactorGeneration.ToString(CultureInfo.InvariantCulture)),
            new(Mfa, proof.MfaSatisfied ? "true" : "false"), new(Route, "local"),
            new(Client, proof.ClientID), new(Resource, proof.Audience),
            new(External, proof.External ? "true" : "false"),
            new("auth_time", proof.AuthenticatedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
        };
        if (proof.AppBinding is not null) common.Add(new(AppBinding, proof.AppBinding));
        if (proof.LegacyCompatibilityExpiresAt is { } legacyDeadline)
            common.Add(new(LegacyCompatibility, legacyDeadline.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        using var rsa = RSA.Create();
        rsa.ImportRSAPrivateKey(options.AccessPrivateKey, out _);
        var accessClaims = common.Concat(decision.Claims).Append(new Claim(Purpose, "access"));
        var accessExpiresAt = Deadline(now, options.AccessLifetimeSeconds, proof.LegacyCompatibilityExpiresAt);
        var refreshExpiresAt = Deadline(now, options.RefreshLifetimeSeconds, proof.LegacyCompatibilityExpiresAt);
        var access = Encode(accessClaims, proof.Audience, now, accessExpiresAt,
            new SigningCredentials(new RsaSecurityKey(rsa)
            {
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
            }, SecurityAlgorithms.RsaSha256));
        var refresh = Encode(common.Append(new Claim(Purpose, "refresh")), options.RefreshAudience, now, refreshExpiresAt,
            new SigningCredentials(new SymmetricSecurityKey(options.RefreshKey), SecurityAlgorithms.HmacSha512));
        return new TokenDTO
        {
            Token = access, RefreshToken = refresh,
            TokenLifeTimeInSeconds = Lifetime(now, accessExpiresAt, options.AccessLifetimeSeconds, proof.LegacyCompatibilityExpiresAt),
            RefreshTokenLifeTimeInSeconds = Lifetime(now, refreshExpiresAt, options.RefreshLifetimeSeconds, proof.LegacyCompatibilityExpiresAt),
            UserData = new TokenUserDataDTO { ID = proof.UserID.ToString(CultureInfo.InvariantCulture),
                Username = decision.Username, FullName = decision.FullName, CompanyType = decision.CompanyType,
                Emails = decision.Email is null ? null! : [new EmailDTO { Email = decision.Email }],
                Phones = decision.Phone is null ? null! : [new PhoneDTO { Phone = decision.Phone }],
                UserSignature = string.IsNullOrWhiteSpace(decision.Signature) ? null :
                    JsonSerializer.Deserialize<IEnumerable<ShiftSoftware.ShiftEntity.Model.Dtos.ShiftFileDTO>>(decision.Signature) }
        };
    }

    public SessionProof? ValidateRefresh(string token, AuthenticationClient client) => ValidateCredential(token, client, false)?.Proof;

    // The deployed refresh request has no AppId. Read its context only after signature and schema validation.
    public SessionProof? ValidateRefresh(string token) => ValidateCredential(token, null, false)?.Proof;

    public SignedInContext? ValidateAccess(string token, AuthenticationClient client) => ValidateCredential(token, client, true);

    private SignedInContext? ValidateCredential(string token, AuthenticationClient? client, bool access)
    {
        try
        {
            if (string.IsNullOrEmpty(token) || token.Length > 16384) return null;
            using var rsa = RSA.Create();
            if (access) rsa.ImportRSAPrivateKey(options.AccessPrivateKey, out _);
            SecurityKey key = access ? new RsaSecurityKey(rsa)
            {
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
            } : new SymmetricSecurityKey(options.RefreshKey);
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 16384 };
            // JWT libraries may collapse duplicate JSON properties. Refuse them before relying on claims.
            var pieces = token.Split('.');
            if (pieces.Length != 3) return null;
            using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(pieces[1]));
            if (payload.RootElement.ValueKind != JsonValueKind.Object) return null;
            var names = payload.RootElement.EnumerateObject().Select(x => x.Name).ToArray();
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Length) return null;
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidIssuer = options.Issuer, ValidAudience = access ? client!.Audience : options.RefreshAudience,
                IssuerSigningKey = key,
                ValidateIssuer = true, ValidateAudience = true, ValidateIssuerSigningKey = true,
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ValidAlgorithms = [access ? SecurityAlgorithms.RsaSha256 : SecurityAlgorithms.HmacSha512], ClockSkew = TimeSpan.Zero,
                LifetimeValidator = (nbf, exp, _, _) =>
                    nbf is not null && exp is not null && nbf <= clock.GetUtcNow().UtcDateTime && exp > clock.GetUtcNow().UtcDateTime
            }, out _);
            string? Single(string type) => principal.FindAll(type).Select(c => c.Value).ToArray() is [var value] ? value : null;
            if (Single(Client) is not { Length: > 0 and <= 255 } clientID || string.IsNullOrWhiteSpace(clientID) ||
                Single(Resource) is not { Length: > 0 and <= 255 } audience || string.IsNullOrWhiteSpace(audience) ||
                Single(External) is not ("true" or "false")) return null;
            client ??= new(clientID, audience, Single(External) == "true");
            if (Single(Schema) != "2" || Single(Purpose) != (access ? "access" : "refresh") || Single(Route) != "local" ||
                Single(Client) != client.ID || Single(Resource) != client.Audience ||
                Single(External) != (client.External ? "true" : "false")) return null;
            if (string.IsNullOrWhiteSpace(Single("sub")) || !long.TryParse(Single(UserID), out var userID) || userID <= 0 ||
                !long.TryParse(Single(Version), out var version) || version < 1 ||
                !long.TryParse(Single(Policy), out var policy) || policy < 1 ||
                !long.TryParse(Single(Factor), out var factor) || factor < 1 ||
                !long.TryParse(Single("auth_time"), out var authenticated) || !long.TryParse(Single("exp"), out var expiry) ||
                Single(Mfa) is not ("true" or "false")) return null;
            var authTime = DateTimeOffset.FromUnixTimeSeconds(authenticated);
            if (authTime > clock.GetUtcNow()) return null;
            var appBindings = principal.FindAll(AppBinding).Select(c => c.Value).ToArray();
            if (appBindings.Length > 1 || (appBindings.Length == 1 &&
                (!client.External || appBindings[0].Length != 64 || !appBindings[0].All(char.IsAsciiHexDigit)))) return null;
            var compatibilityClaims = principal.FindAll(LegacyCompatibility).Select(c => c.Value).ToArray();
            DateTimeOffset? compatibilityDeadline = null;
            if (compatibilityClaims.Length > 1) return null;
            if (compatibilityClaims.Length == 1)
            {
                if (!long.TryParse(compatibilityClaims[0], NumberStyles.None, CultureInfo.InvariantCulture, out var deadlineSeconds))
                    return null;
                compatibilityDeadline = DateTimeOffset.FromUnixTimeSeconds(deadlineSeconds);
                if (compatibilityDeadline <= clock.GetUtcNow() || DateTimeOffset.FromUnixTimeSeconds(expiry) > compatibilityDeadline)
                    return null;
            }
            return new(new(userID, version, policy, factor, Single(Mfa) == "true", authTime, client.ID, client.Audience,
                client.External, Single("sub")!, appBindings.SingleOrDefault(), compatibilityDeadline), DateTimeOffset.FromUnixTimeSeconds(expiry));
        }
        catch (Exception e) when (e is SecurityTokenException or ArgumentException or FormatException or JsonException)
        {
            return null;
        }
    }

    private string Encode(IEnumerable<Claim> claims, string audience, DateTimeOffset now, DateTimeOffset expiresAt, SigningCredentials key)
    {
        var jwt = new JwtSecurityToken(options.Issuer, audience,
            claims.Append(new("jti", Guid.NewGuid().ToString("N"))).Append(new("iat", now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)),
            now.UtcDateTime, expiresAt.UtcDateTime, key);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static DateTimeOffset Deadline(DateTimeOffset now, int lifetimeSeconds, DateTimeOffset? compatibilityDeadline) =>
        compatibilityDeadline is { } deadline && deadline < now.AddSeconds(lifetimeSeconds)
            ? deadline : now.AddSeconds(lifetimeSeconds);

    private static long Lifetime(DateTimeOffset now, DateTimeOffset expiresAt, int configured, DateTimeOffset? compatibilityDeadline) =>
        compatibilityDeadline is null ? configured : Math.Max(0, expiresAt.ToUnixTimeSeconds() - now.ToUnixTimeSeconds());
}
