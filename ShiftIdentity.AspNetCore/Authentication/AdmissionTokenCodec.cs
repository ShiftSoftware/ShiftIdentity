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

    public TokenDTO Issue(IssuanceDecision decision)
    {
        var proof = decision.Proof;
        if (proof.UserID <= 0 || string.IsNullOrWhiteSpace(proof.Subject) || proof.SecurityVersion < 1 || proof.PolicyRevision != options.PolicyRevision ||
            options.AccessLifetimeSeconds is < 1 or > 900 || options.RefreshLifetimeSeconds < 1 ||
            options.RefreshKey.Length < 64 || options.OperationKey.Length < 32)
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
        using var rsa = RSA.Create();
        rsa.ImportRSAPrivateKey(options.AccessPrivateKey, out _);
        var accessClaims = common.Concat(decision.Claims).Append(new Claim(Purpose, "access"));
        var access = Encode(accessClaims, proof.Audience, now, options.AccessLifetimeSeconds,
            new SigningCredentials(new RsaSecurityKey(rsa)
            {
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
            }, SecurityAlgorithms.RsaSha256));
        var refresh = Encode(common.Append(new Claim(Purpose, "refresh")), options.RefreshAudience, now,
            options.RefreshLifetimeSeconds,
            new SigningCredentials(new SymmetricSecurityKey(options.RefreshKey), SecurityAlgorithms.HmacSha512));
        return new TokenDTO
        {
            Token = access, RefreshToken = refresh, TokenLifeTimeInSeconds = options.AccessLifetimeSeconds,
            RefreshTokenLifeTimeInSeconds = options.RefreshLifetimeSeconds,
            UserData = new TokenUserDataDTO { ID = proof.UserID.ToString(CultureInfo.InvariantCulture),
                Username = decision.Username, FullName = decision.FullName }
        };
    }

    public SessionProof? ValidateRefresh(string token, AuthenticationClient client)
    {
        try
        {
            if (token.Length > 16384) return null;
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
                ValidIssuer = options.Issuer, ValidAudience = options.RefreshAudience,
                IssuerSigningKey = new SymmetricSecurityKey(options.RefreshKey),
                ValidateIssuer = true, ValidateAudience = true, ValidateIssuerSigningKey = true,
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha512], ClockSkew = TimeSpan.Zero,
                LifetimeValidator = (nbf, exp, _, _) =>
                    nbf is not null && exp is not null && nbf <= clock.GetUtcNow().UtcDateTime && exp > clock.GetUtcNow().UtcDateTime
            }, out _);
            string? Single(string type) => principal.FindAll(type).Select(c => c.Value).ToArray() is [var value] ? value : null;
            if (Single(Schema) != "2" || Single(Purpose) != "refresh" || Single(Route) != "local" ||
                Single(Client) != client.ID || Single(Resource) != client.Audience ||
                Single(External) != (client.External ? "true" : "false")) return null;
            if (string.IsNullOrWhiteSpace(Single("sub")) || !long.TryParse(Single(UserID), out var userID) || userID <= 0 ||
                !long.TryParse(Single(Version), out var version) || version < 1 ||
                !long.TryParse(Single(Policy), out var policy) || policy < 1 ||
                !long.TryParse(Single(Factor), out var factor) || factor < 1 ||
                !long.TryParse(Single("auth_time"), out var authenticated) ||
                Single(Mfa) is not ("true" or "false")) return null;
            var authTime = DateTimeOffset.FromUnixTimeSeconds(authenticated);
            if (authTime > clock.GetUtcNow()) return null;
            return new(userID, version, policy, factor, Single(Mfa) == "true", authTime, client.ID, client.Audience, client.External, Single("sub")!);
        }
        catch (Exception e) when (e is SecurityTokenException or ArgumentException or FormatException or JsonException)
        {
            return null;
        }
    }

    private string Encode(IEnumerable<Claim> claims, string audience, DateTimeOffset now, int seconds, SigningCredentials key)
    {
        var jwt = new JwtSecurityToken(options.Issuer, audience,
            claims.Append(new("jti", Guid.NewGuid().ToString("N"))).Append(new("iat", now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)),
            now.UtcDateTime, now.AddSeconds(seconds).UtcDateTime, key);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
