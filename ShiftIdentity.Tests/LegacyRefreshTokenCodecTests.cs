using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Models;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Policy")]
public sealed class LegacyRefreshTokenCodecTests
{
    private readonly ControlledClock clock = new(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
    private readonly RefreshTokenSettingsModel settings = new()
    {
        Issuer = "https://legacy.invalid", Audience = "legacy-refresh", ExpireSeconds = 1800,
        Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
    };

    [Theory]
    [InlineData("123")]
    [InlineData("AbCdEf123")]
    public void Valid_legacy_subject_formats_retain_the_exact_deadline(string subject)
    {
        var deadline = clock.GetUtcNow().AddMinutes(30);
        var proof = Assert.IsType<LegacyRefreshTokenCodec.Proof>(Codec().Validate(Token(subject, deadline)));
        Assert.Equal(subject, proof.Subject);
        Assert.Equal(deadline, proof.ExpiresAt);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("future-not-before")]
    [InlineData("excess-lifetime")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-key")]
    [InlineData("wrong-algorithm")]
    [InlineData("jose-hs512")]
    [InlineData("v2-shaped")]
    [InlineData("unknown-claim")]
    [InlineData("duplicate-subject")]
    public void Invalid_key_format_or_remaining_lifetime_is_refused(string scenario)
    {
        var deadline = clock.GetUtcNow().AddMinutes(20);
        var issuer = settings.Issuer; var audience = settings.Audience; var key = settings.Key;
        var algorithm = SecurityAlgorithms.HmacSha512Signature;
        DateTimeOffset? notBefore = null;
        var extra = new List<Claim>();
        switch (scenario)
        {
            case "expired": deadline = clock.GetUtcNow(); break;
            case "future-not-before": notBefore = clock.GetUtcNow().AddSeconds(1); break;
            case "excess-lifetime": deadline = clock.GetUtcNow().AddSeconds(settings.ExpireSeconds + 1); break;
            case "wrong-issuer": issuer = "https://other.invalid"; break;
            case "wrong-audience": audience = "other"; break;
            case "wrong-key": key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)); break;
            case "wrong-algorithm": algorithm = SecurityAlgorithms.HmacSha256Signature; break;
            case "jose-hs512": algorithm = SecurityAlgorithms.HmacSha512; break;
            case "v2-shaped": extra.Add(new("shift_schema", "2")); break;
            case "unknown-claim": extra.Add(new("custom", "value")); break;
            case "duplicate-subject": extra.Add(new("sub", "456")); break;
        }
        Assert.Null(Codec().Validate(Token("123", deadline, notBefore, issuer, audience, key, algorithm, extra)));
    }

    [Fact]
    public void Missing_configuration_and_malformed_or_oversized_tokens_are_refused()
    {
        Assert.Null(LegacyRefreshTokenCodec.TryCreate(null, clock));
        Assert.Null(LegacyRefreshTokenCodec.TryCreate(new(), clock));
        Assert.Null(Codec().Validate("not-a-token"));
        Assert.Null(Codec().Validate(new string('x', 16385)));
    }

    private LegacyRefreshTokenCodec Codec() => new(settings, clock);

    private string Token(string subject, DateTimeOffset expires, DateTimeOffset? notBefore = null,
        string? issuer = null, string? audience = null, string? key = null,
        string algorithm = SecurityAlgorithms.HmacSha512Signature, IEnumerable<Claim>? extra = null)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, subject) }.Concat(extra ?? []);
        var jwt = new JwtSecurityToken(issuer ?? settings.Issuer, audience ?? settings.Audience, claims,
            notBefore?.UtcDateTime, expires.UtcDateTime,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key ?? settings.Key)), algorithm));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
