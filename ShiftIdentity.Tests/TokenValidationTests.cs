using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Policy")]
public sealed class TokenValidationTests
{
    private readonly ControlledClock clock = new(new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero));
    private readonly AuthenticationClient client = new("test-client", "test-api");
    private readonly IdentityAdmissionOptions options;

    public TokenValidationTests()
    {
        using var rsa = RSA.Create(2048);
        options = new("https://identity.invalid", "identity-refresh", rsa.ExportRSAPrivateKey(),
            RandomNumberGenerator.GetBytes(64), RandomNumberGenerator.GetBytes(32));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Valid_credentials_retain_bound_subject_client_version_and_proof(bool access)
    {
        var result = Validate(Sign(Payload(access), access), access);
        Assert.Equal(new SessionProof(42, 3, 1, 1, true, clock.GetUtcNow().AddSeconds(-10),
            client.ID, client.Audience, false, "encoded-user"), result);
    }

    public static IEnumerable<object[]> InvalidClaimCases => ForBothKinds(
        "version-missing", "version-zero", "version-negative", "version-overflow", "version-text",
        "user-missing", "user-zero", "subject-missing", "subject-blank", "schema", "purpose",
        "issuer", "audience", "client", "resource", "external", "mfa", "factor", "policy", "route",
        "expired", "future", "not-before-missing", "auth-time-missing", "auth-time-future",
        "auth-time-out-of-range", "expiry-missing", "expiry-out-of-range");

    [Theory]
    [MemberData(nameof(InvalidClaimCases))]
    public void Invalid_claims_are_refused_even_when_the_signature_is_valid(bool access, string scenario)
    {
        var payload = Payload(access);
        switch (scenario)
        {
            case "version-missing": payload.Remove("shift_sv"); break;
            case "version-zero": payload["shift_sv"] = "0"; break;
            case "version-negative": payload["shift_sv"] = "-1"; break;
            case "version-overflow": payload["shift_sv"] = "9223372036854775808"; break;
            case "version-text": payload["shift_sv"] = "invalid"; break;
            case "user-missing": payload.Remove("shift_uid"); break;
            case "user-zero": payload["shift_uid"] = "0"; break;
            case "subject-missing": payload.Remove("sub"); break;
            case "subject-blank": payload["sub"] = " "; break;
            case "schema": payload["shift_schema"] = "1"; break;
            case "purpose": payload["shift_purpose"] = access ? "refresh" : "access"; break;
            case "issuer": payload["iss"] = "https://wrong.invalid"; break;
            case "audience": payload["aud"] = "wrong"; break;
            case "client": payload["shift_client"] = "other-client"; break;
            case "resource": payload["shift_resource"] = "other-api"; break;
            case "external": payload["shift_external"] = "true"; break;
            case "mfa": payload["shift_mfa"] = "yes"; break;
            case "factor": payload["shift_factor"] = "0"; break;
            case "policy": payload["shift_policy"] = "0"; break;
            case "route": payload["shift_route"] = "provider"; break;
            case "expired": payload["exp"] = clock.GetUtcNow().ToUnixTimeSeconds(); break;
            case "future": payload["nbf"] = clock.GetUtcNow().AddSeconds(1).ToUnixTimeSeconds(); break;
            case "not-before-missing": payload.Remove("nbf"); break;
            case "auth-time-missing": payload.Remove("auth_time"); break;
            case "auth-time-future": payload["auth_time"] = clock.GetUtcNow().AddSeconds(1).ToUnixTimeSeconds().ToString(); break;
            case "auth-time-out-of-range": payload["auth_time"] = long.MinValue.ToString(); break;
            case "expiry-missing": payload.Remove("exp"); break;
            case "expiry-out-of-range": payload["exp"] = long.MaxValue; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        Assert.Null(Validate(Sign(payload, access), access));
    }

    public static IEnumerable<object[]> MissingBindingCases => ForBothKinds(
        "iss", "aud", "shift_schema", "shift_purpose", "shift_client", "shift_resource",
        "shift_external", "shift_mfa", "shift_factor", "shift_policy", "shift_route");

    [Theory]
    [MemberData(nameof(MissingBindingCases))]
    public void Missing_bindings_are_not_filled_from_server_defaults(bool access, string claim)
    {
        var payload = Payload(access);
        payload.Remove(claim);
        Assert.Null(Validate(Sign(payload, access), access));
    }

    public static IEnumerable<object[]> InvalidSignatureCases => ForBothKinds(
        "duplicate", "array", "unsigned", "wrong-key", "payload-tampered", "signature-tampered");

    [Theory]
    [MemberData(nameof(InvalidSignatureCases))]
    public void Ambiguous_or_invalid_signatures_are_refused(bool access, string scenario)
    {
        var payload = Payload(access);
        using var otherRsa = RSA.Create(2048);
        var token = scenario switch
        {
            "duplicate" => SignJson(JsonSerializer.Serialize(payload).Replace("\"shift_sv\":\"3\"", "\"shift_sv\":\"3\",\"shift_sv\":\"4\""), access),
            "array" => SignJson(JsonSerializer.Serialize(payload).Replace("\"shift_sv\":\"3\"", "\"shift_sv\":[\"3\",\"4\"]"), access),
            "unsigned" => SignJson(JsonSerializer.Serialize(payload), access, "none"),
            "wrong-key" => SignJson(JsonSerializer.Serialize(payload), access,
                key: access ? otherRsa.ExportRSAPrivateKey() : RandomNumberGenerator.GetBytes(64)),
            "payload-tampered" => TamperPayload(Sign(payload, access)),
            "signature-tampered" => TamperSignature(Sign(payload, access)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        Assert.Null(Validate(token, access));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Correct_signatures_with_disallowed_algorithms_are_refused(bool access)
    {
        var algorithm = access ? "RS512" : "HS256";
        var token = SignJson(JsonSerializer.Serialize(Payload(access)), access, algorithm);
        using var rsa = RSA.Create();
        rsa.ImportRSAPrivateKey(options.AccessPrivateKey, out _);
        SecurityKey key = access ? new RsaSecurityKey(rsa.ExportParameters(false)) : new SymmetricSecurityKey(options.RefreshKey);
        AssertSignatureValid(token, key, algorithm);
        Assert.Null(Validate(token, access));
    }

    [Theory]
    [InlineData(false, "HS512")]
    [InlineData(true, "HS256")]
    [InlineData(true, "HS512")]
    public void Access_claims_signed_with_a_symmetric_key_cannot_be_used_as_access(bool useRsaPublicKey, string algorithm)
    {
        using var rsa = RSA.Create();
        rsa.ImportRSAPrivateKey(options.AccessPrivateKey, out _);
        var key = useRsaPublicKey ? rsa.ExportSubjectPublicKeyInfo() : options.RefreshKey;
        var token = SignJson(JsonSerializer.Serialize(Payload(true)), false, algorithm, key);
        AssertSignatureValid(token, new SymmetricSecurityKey(key), algorithm);
        Assert.Null(new AdmissionTokenCodec(options, clock).ValidateAccess(token, client));
    }

    public static IEnumerable<object[]> MalformedTokenCases => ForBothKinds(
        "null", "empty", "missing-segments", "extra-segment", "base64", "json", "array", "missing-signature", "oversized");

    [Theory]
    [MemberData(nameof(MalformedTokenCases))]
    public void Malformed_tokens_are_refused_without_throwing(bool access, string scenario)
    {
        var valid = Sign(Payload(access), access);
        Assert.NotNull(Validate(valid, access));
        var pieces = valid.Split('.');
        var token = scenario switch
        {
            "null" => null,
            "empty" => "",
            "missing-segments" => "malformed",
            "extra-segment" => valid + ".extra",
            "base64" => pieces[0] + ".%." + pieces[2],
            "json" => SignJson("{", access),
            "array" => SignJson("[]", access),
            "missing-signature" => pieces[0] + "." + pieces[1] + ".",
            "oversized" => Sign(new Dictionary<string, object>(Payload(access)) { ["padding"] = new string('x', 16384) }, access),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        Assert.Null(Validate(token!, access));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lifetime_boundaries_are_enforced_without_clock_skew(bool access)
    {
        var payload = Payload(access);
        payload["nbf"] = clock.GetUtcNow().AddSeconds(1).ToUnixTimeSeconds();
        payload["exp"] = clock.GetUtcNow().AddSeconds(2).ToUnixTimeSeconds();
        var token = Sign(payload, access);
        Assert.Null(Validate(token, access));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.NotNull(Validate(token, access));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(Validate(token, access));
    }

    [Fact]
    public void Access_uses_an_independent_key_purpose_and_capped_lifetime()
    {
        var codec = new AdmissionTokenCodec(options, clock);
        var proof = new SessionProof(42, 3, 1, 1, true, clock.GetUtcNow(), client.ID, client.Audience, false, "encoded-user");
        var tokens = codec.Issue(new(proof, "synthetic", "Synthetic", Array.Empty<Claim>(), clock.GetUtcNow()));
        Assert.Equal(new SignedInContext(proof, clock.GetUtcNow().AddMinutes(15)), codec.ValidateAccess(tokens.Token, client));
        Assert.Equal(proof, codec.ValidateRefresh(tokens.RefreshToken, client));
        Assert.Null(codec.ValidateRefresh(tokens.Token, client));
        Assert.Null(codec.ValidateAccess(tokens.RefreshToken, client));
        using var rsa = RSA.Create();
        rsa.ImportRSAPrivateKey(options.AccessPrivateKey, out _);
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler { MapInboundClaims = false };
        handler.ValidateToken(tokens.Token, new TokenValidationParameters
        {
            ValidIssuer = options.Issuer, ValidAudience = client.Audience,
            IssuerSigningKey = new RsaSecurityKey(rsa.ExportParameters(false)),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ValidateLifetime = false
        }, out var validated);
        Assert.Equal(clock.GetUtcNow().AddMinutes(15).UtcDateTime, validated.ValidTo);
        Assert.Throws<InvalidOperationException>(() => new AdmissionTokenCodec(options with { AccessLifetimeSeconds = 901 }, clock)
            .Issue(new(proof, "synthetic", "Synthetic", Array.Empty<Claim>(), clock.GetUtcNow())));
    }

    [Fact]
    public void Legacy_compatibility_context_is_signed_preserved_and_caps_both_credentials()
    {
        var deadline = clock.GetUtcNow().AddMinutes(4);
        var proof = new SessionProof(42, 3, 1, 1, false, DateTimeOffset.UnixEpoch,
            client.ID, client.Audience, false, "encoded-user", LegacyCompatibilityExpiresAt: deadline);
        var codec = new AdmissionTokenCodec(options, clock);
        var tokens = codec.Issue(new(proof, "synthetic", "Synthetic", Array.Empty<Claim>(), clock.GetUtcNow()));
        Assert.Equal(240, tokens.TokenLifeTimeInSeconds);
        Assert.Equal(240, tokens.RefreshTokenLifeTimeInSeconds);
        Assert.Equal(proof, codec.ValidateAccess(tokens.Token, client)!.Proof);
        Assert.Equal(proof, codec.ValidateRefresh(tokens.RefreshToken, client));
        Assert.Equal(deadline.UtcDateTime, new JwtSecurityTokenHandler().ReadJwtToken(tokens.Token).ValidTo);
        Assert.Equal(deadline.UtcDateTime, new JwtSecurityTokenHandler().ReadJwtToken(tokens.RefreshToken).ValidTo);
        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Null(codec.ValidateAccess(tokens.Token, client));
        Assert.Null(codec.ValidateRefresh(tokens.RefreshToken, client));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("past")]
    [InlineData("before-expiry")]
    public void Legacy_compatibility_context_must_be_an_unambiguous_covering_deadline(string scenario)
    {
        var payload = Payload(false);
        payload["shift_legacy_until"] = clock.GetUtcNow().AddMinutes(20).ToUnixTimeSeconds().ToString();
        Assert.NotNull(new AdmissionTokenCodec(options, clock).ValidateRefresh(Sign(payload, false), client));
        payload["shift_legacy_until"] = scenario switch
        {
            "invalid" => "not-a-time",
            "past" => clock.GetUtcNow().ToUnixTimeSeconds().ToString(),
            _ => clock.GetUtcNow().AddMinutes(5).ToUnixTimeSeconds().ToString()
        };
        Assert.Null(new AdmissionTokenCodec(options, clock).ValidateRefresh(Sign(payload, false), client));
    }

    private static IEnumerable<object[]> ForBothKinds(params string[] scenarios) =>
        scenarios.SelectMany(scenario => new[] { new object[] { false, scenario }, new object[] { true, scenario } });

    [Theory]
    [InlineData("client-missing")]
    [InlineData("client-blank")]
    [InlineData("resource-array")]
    [InlineData("external-invalid")]
    [InlineData("binding-short")]
    [InlineData("binding-array")]
    [InlineData("binding-internal")]
    [InlineData("wrong-key")]
    [InlineData("tampered")]
    [InlineData("expired")]
    public void Compatible_refresh_context_requires_valid_signed_unambiguous_claims(string scenario)
    {
        var payload = Payload(false);
        payload["shift_client"] = "destination";
        payload["shift_resource"] = "destination";
        payload["shift_external"] = "true";
        payload["shift_app"] = new string('A', 64);
        var codec = new AdmissionTokenCodec(options, clock);
        var valid = codec.ValidateRefresh(Sign(payload, false));
        Assert.NotNull(valid);
        Assert.Equal("destination", valid.ClientID);
        Assert.Equal("destination", valid.Audience);
        Assert.Equal(new string('A', 64), valid.AppBinding);
        switch (scenario)
        {
            case "client-missing": payload.Remove("shift_client"); break;
            case "client-blank": payload["shift_client"] = " "; break;
            case "resource-array": payload["shift_resource"] = new[] { "destination", "different" }; break;
            case "external-invalid": payload["shift_external"] = "yes"; break;
            case "binding-short": payload["shift_app"] = "AAA"; break;
            case "binding-array": payload["shift_app"] = new[] { new string('A', 64), new string('B', 64) }; break;
            case "binding-internal": payload["shift_external"] = "false"; break;
            case "expired": payload["exp"] = clock.GetUtcNow().ToUnixTimeSeconds(); break;
        }
        var token = scenario switch
        {
            "wrong-key" => SignJson(JsonSerializer.Serialize(payload), false, key: RandomNumberGenerator.GetBytes(64)),
            "tampered" => TamperPayload(Sign(payload, false)),
            _ => Sign(payload, false)
        };
        Assert.Null(codec.ValidateRefresh(token));
    }

    private SessionProof? Validate(string token, bool access)
    {
        var codec = new AdmissionTokenCodec(options, clock);
        return access ? codec.ValidateAccess(token, client)?.Proof : codec.ValidateRefresh(token, client);
    }

    private Dictionary<string, object> Payload(bool access) => new()
    {
        ["iss"] = options.Issuer, ["aud"] = access ? client.Audience : options.RefreshAudience, ["sub"] = "encoded-user",
        ["shift_uid"] = "42", ["shift_schema"] = "2", ["shift_sv"] = "3", ["shift_policy"] = "1",
        ["shift_factor"] = "1", ["shift_mfa"] = "true", ["shift_route"] = "local",
        ["shift_client"] = client.ID, ["shift_resource"] = client.Audience,
        ["shift_external"] = "false", ["shift_purpose"] = access ? "access" : "refresh",
        ["auth_time"] = clock.GetUtcNow().AddSeconds(-10).ToUnixTimeSeconds().ToString(),
        ["nbf"] = clock.GetUtcNow().AddSeconds(-10).ToUnixTimeSeconds(),
        ["exp"] = clock.GetUtcNow().AddMinutes(10).ToUnixTimeSeconds()
    };

    private string Sign(Dictionary<string, object> payload, bool access) => SignJson(JsonSerializer.Serialize(payload), access);
    private string SignJson(string json, bool access, string? algorithm = null, byte[]? key = null)
    {
        algorithm ??= access ? "RS256" : "HS512";
        var input = Base64UrlEncoder.Encode(JsonSerializer.Serialize(new { alg = algorithm, typ = "JWT" })) + "." + Base64UrlEncoder.Encode(json);
        var bytes = Encoding.ASCII.GetBytes(input);
        if (algorithm == "none") return input + ".";
        byte[] signature;
        if (access)
        {
            using var rsa = RSA.Create();
            rsa.ImportRSAPrivateKey(key ?? options.AccessPrivateKey, out _);
            signature = rsa.SignData(bytes, algorithm switch
            {
                "RS256" => HashAlgorithmName.SHA256,
                "RS512" => HashAlgorithmName.SHA512,
                _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
            }, RSASignaturePadding.Pkcs1);
        }
        else
        {
            signature = algorithm switch
            {
                "HS512" => HMACSHA512.HashData(key ?? options.RefreshKey, bytes),
                "HS256" => HMACSHA256.HashData(key ?? options.RefreshKey, bytes),
                _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
            };
        }
        return input + "." + Base64UrlEncoder.Encode(signature);
    }

    private static void AssertSignatureValid(string token, SecurityKey key, string algorithm)
    {
        // Prove rejection is due to the credential profile, rather than a broken test signature.
        new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
        {
            IssuerSigningKey = key, ValidAlgorithms = [algorithm], RequireSignedTokens = true,
            ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = false
        }, out _);
    }

    private static string TamperPayload(string token)
    {
        var pieces = token.Split('.');
        pieces[1] = Base64UrlEncoder.Encode(Base64UrlEncoder.Decode(pieces[1]).Replace("\"shift_sv\":\"3\"", "\"shift_sv\":\"4\""));
        return string.Join('.', pieces);
    }

    private static string TamperSignature(string token)
    {
        var pieces = token.Split('.');
        var bytes = Base64UrlEncoder.DecodeBytes(pieces[2]);
        bytes[0] ^= 1;
        pieces[2] = Base64UrlEncoder.Encode(bytes);
        return string.Join('.', pieces);
    }
}
