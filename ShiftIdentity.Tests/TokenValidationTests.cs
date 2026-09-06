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

    [Fact]
    public void Valid_refresh_retains_bound_subject_client_version_and_proof()
    {
        var result = new AdmissionTokenCodec(options, clock).ValidateRefresh(Sign(Payload()), client);
        Assert.NotNull(result);
        Assert.Equal(42, result.UserID);
        Assert.Equal("encoded-user", result.Subject);
        Assert.Equal(3, result.SecurityVersion);
        Assert.True(result.MfaSatisfied);
        Assert.Equal("test-client", result.ClientID);
    }

    [Theory]
    [InlineData("version-missing")]
    [InlineData("version-zero")]
    [InlineData("user-missing")]
    [InlineData("user-zero")]
    [InlineData("subject-missing")]
    [InlineData("schema")]
    [InlineData("purpose")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("client")]
    [InlineData("resource")]
    [InlineData("external")]
    [InlineData("mfa")]
    [InlineData("factor")]
    [InlineData("policy")]
    [InlineData("route")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("auth-time-missing")]
    [InlineData("auth-time-future")]
    [InlineData("expiry-missing")]
    public void Invalid_claims_are_refused_even_when_the_signature_is_valid(string scenario)
    {
        var payload = Payload();
        switch (scenario)
        {
            case "version-missing": payload.Remove("shift_sv"); break;
            case "version-zero": payload["shift_sv"] = "0"; break;
            case "user-missing": payload.Remove("shift_uid"); break;
            case "user-zero": payload["shift_uid"] = "0"; break;
            case "subject-missing": payload.Remove("sub"); break;
            case "schema": payload["shift_schema"] = "1"; break;
            case "purpose": payload["shift_purpose"] = "access"; break;
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
            case "auth-time-missing": payload.Remove("auth_time"); break;
            case "auth-time-future": payload["auth_time"] = clock.GetUtcNow().AddSeconds(1).ToUnixTimeSeconds().ToString(); break;
            case "expiry-missing": payload.Remove("exp"); break;
        }
        Assert.Null(new AdmissionTokenCodec(options, clock).ValidateRefresh(Sign(payload), client));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("array")]
    [InlineData("unsigned")]
    [InlineData("wrong-key")]
    [InlineData("wrong-algorithm")]
    [InlineData("malformed")]
    public void Ambiguous_or_invalid_signatures_are_refused(string scenario)
    {
        var payload = Payload();
        var token = scenario switch
        {
            "duplicate" => SignJson(JsonSerializer.Serialize(payload).Replace("\"shift_sv\":\"3\"", "\"shift_sv\":\"3\",\"shift_sv\":\"4\"")),
            "array" => SignJson(JsonSerializer.Serialize(payload).Replace("\"shift_sv\":\"3\"", "\"shift_sv\":[\"3\",\"4\"]")),
            "unsigned" => SignJson(JsonSerializer.Serialize(payload), "none"),
            "wrong-key" => Sign(payload, RandomNumberGenerator.GetBytes(64)),
            "wrong-algorithm" => SignJson(JsonSerializer.Serialize(payload), "HS256"),
            _ => "malformed"
        };
        Assert.Null(new AdmissionTokenCodec(options, clock).ValidateRefresh(token, client));
    }

    [Fact]
    public void Access_uses_an_independent_key_purpose_and_capped_lifetime()
    {
        var codec = new AdmissionTokenCodec(options, clock);
        var proof = new SessionProof(42, 3, 1, 1, true, clock.GetUtcNow(), client.ID, client.Audience, false, "encoded-user");
        var tokens = codec.Issue(new(proof, "synthetic", "Synthetic", Array.Empty<Claim>(), clock.GetUtcNow()));
        Assert.Null(codec.ValidateRefresh(tokens.Token, client));
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

    private Dictionary<string, object> Payload() => new()
    {
        ["iss"] = options.Issuer, ["aud"] = options.RefreshAudience, ["sub"] = "encoded-user",
        ["shift_uid"] = "42", ["shift_schema"] = "2", ["shift_sv"] = "3", ["shift_policy"] = "1",
        ["shift_factor"] = "1", ["shift_mfa"] = "true", ["shift_route"] = "local",
        ["shift_client"] = client.ID, ["shift_resource"] = client.Audience,
        ["shift_external"] = "false", ["shift_purpose"] = "refresh",
        ["auth_time"] = clock.GetUtcNow().AddSeconds(-10).ToUnixTimeSeconds().ToString(),
        ["nbf"] = clock.GetUtcNow().AddSeconds(-10).ToUnixTimeSeconds(),
        ["exp"] = clock.GetUtcNow().AddMinutes(10).ToUnixTimeSeconds()
    };

    private string Sign(Dictionary<string, object> payload, byte[]? key = null) => SignJson(JsonSerializer.Serialize(payload), key: key);
    private string SignJson(string json, string algorithm = "HS512", byte[]? key = null)
    {
        var input = Base64UrlEncoder.Encode(JsonSerializer.Serialize(new { alg = algorithm, typ = "JWT" })) + "." + Base64UrlEncoder.Encode(json);
        return input + "." + (algorithm == "none" ? "" : Base64UrlEncoder.Encode(HMACSHA512.HashData(key ?? options.RefreshKey, Encoding.ASCII.GetBytes(input))));
    }
}
