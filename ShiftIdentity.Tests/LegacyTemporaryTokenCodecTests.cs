using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Policy")]
public sealed class LegacyTemporaryTokenCodecTests
{
    private readonly ControlledClock clock = new(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
    private readonly TemporaryTokenSettingsModel settings = new()
    {
        Issuer = "https://legacy.invalid", Audience = "legacy-temporary", ExpireSeconds = 300,
        Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
    };

    [Theory]
    [InlineData("123", AuthPurpose.Mfa)]
    [InlineData("AbCdEf123", AuthPurpose.Mfa)]
    [InlineData("AbCdEf123", AuthPurpose.ChangePassword)]
    [InlineData("AbCdEf123", AuthPurpose.MfaEnrollment)]
    public void Deployed_step_credential_yields_subject_purpose_and_exact_deadline(string subject, AuthPurpose purpose)
    {
        var deadline = clock.GetUtcNow().AddMinutes(5);
        var proof = Assert.IsType<LegacyTemporaryTokenCodec.Proof>(Codec().Validate(Token(subject, deadline, purpose.ToString())));
        Assert.Equal(subject, proof.Subject);
        Assert.Equal(purpose, proof.Purpose);
        Assert.Equal(deadline, proof.ExpiresAt);
    }

    [Fact]
    public void Credential_signed_by_the_deployed_issuer_validates_with_its_purpose()
    {
        // The real legacy issuer with the settings a pre-cutover host used; only the temporary settings are read.
        var hashIds = new HashIdService(Options.Create(new ShiftEntityOptions()));
        var issuer = new TokenService(new ShiftIdentityConfiguration { TemporaryTokenSettings = settings }, hashIds);
        var user = new User { ID = 42, Username = "synthetic", FullName = "Synthetic User" };
        var mfa = issuer.GenerateMfaToken(user);
        Assert.Equal(AuthPurpose.Mfa, mfa.Flow);
        Assert.Null(mfa.RefreshToken);
        var codec = new LegacyTemporaryTokenCodec(settings, TimeProvider.System);
        var proof = Assert.IsType<LegacyTemporaryTokenCodec.Proof>(codec.Validate(mfa.Token));
        Assert.Equal(AuthPurpose.Mfa, proof.Purpose);
        Assert.Equal(hashIds.Encode<UserDTO>(42), proof.Subject);
        Assert.InRange(proof.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(290), DateTimeOffset.UtcNow.AddSeconds(301));
        Assert.Equal(AuthPurpose.ChangePassword, codec.Validate(issuer.GenerateChangePasswordToken(user).Token)!.Purpose);
        Assert.Equal(AuthPurpose.MfaEnrollment, codec.Validate(issuer.GenerateMfaEnrollmentToken(user).Token)!.Purpose);
        // The deployed validator still accepts the same credential; the codec is stricter, never looser, than it.
        Assert.NotNull(issuer.ValidateTemporaryToken(mfa.Token));
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
    [InlineData("missing-purpose")]
    [InlineData("none-purpose")]
    [InlineData("numeric-purpose")]
    [InlineData("unknown-purpose")]
    [InlineData("duplicate-purpose")]
    public void Invalid_key_format_purpose_or_remaining_lifetime_is_refused(string scenario)
    {
        var deadline = clock.GetUtcNow().AddMinutes(4);
        var issuer = settings.Issuer; var audience = settings.Audience; var key = settings.Key;
        var algorithm = SecurityAlgorithms.HmacSha512Signature;
        DateTimeOffset? notBefore = null;
        string? purpose = AuthPurpose.Mfa.ToString();
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
            case "missing-purpose": purpose = null; break;
            case "none-purpose": purpose = AuthPurpose.None.ToString(); break;
            case "numeric-purpose": purpose = ((int)AuthPurpose.Mfa).ToString(); break;
            case "unknown-purpose": purpose = "Admin"; break;
            case "duplicate-purpose": extra.Add(new(ShiftIdentityClaims.TokenPurpose, AuthPurpose.ChangePassword.ToString())); break;
        }
        Assert.Null(Codec().Validate(Token("123", deadline, purpose, notBefore, issuer, audience, key, algorithm, extra)));
    }

    [Fact]
    public void Missing_configuration_and_malformed_or_oversized_tokens_are_refused()
    {
        Assert.Null(LegacyTemporaryTokenCodec.TryCreate(null, clock));
        Assert.Null(LegacyTemporaryTokenCodec.TryCreate(new(), clock));
        Assert.Null(LegacyTemporaryTokenCodec.TryCreate(new() { Issuer = settings.Issuer, Audience = settings.Audience, Key = settings.Key, ExpireSeconds = 0 }, clock));
        Assert.Null(LegacyTemporaryTokenCodec.TryCreate(new() { Audience = settings.Audience, Key = settings.Key, ExpireSeconds = 300 }, clock));
        Assert.NotNull(LegacyTemporaryTokenCodec.TryCreate(new() { Issuer = settings.Issuer, Key = settings.Key, ExpireSeconds = 300 }, clock));
        Assert.Null(Codec().Validate(null));
        Assert.Null(Codec().Validate(""));
        Assert.Null(Codec().Validate("not-a-token"));
        Assert.Null(Codec().Validate(new string('x', 8193)));
    }

    [Fact]
    public void Audience_is_checked_only_when_one_is_configured()
    {
        var open = new LegacyTemporaryTokenCodec(new() { Issuer = settings.Issuer, Key = settings.Key, ExpireSeconds = 300 }, clock);
        Assert.NotNull(open.Validate(Token("123", clock.GetUtcNow().AddMinutes(4), audience: "anything")));
        Assert.Null(Codec().Validate(Token("123", clock.GetUtcNow().AddMinutes(4), audience: "anything")));
    }

    private LegacyTemporaryTokenCodec Codec() => new(settings, clock);

    private string Token(string subject, DateTimeOffset expires, string? purpose = "Mfa", DateTimeOffset? notBefore = null,
        string? issuer = null, string? audience = null, string? key = null,
        string algorithm = SecurityAlgorithms.HmacSha512Signature, IEnumerable<Claim>? extra = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject), new(ClaimTypes.Name, "synthetic"), new(ClaimTypes.GivenName, "Synthetic")
        };
        if (purpose is not null) claims.Add(new(ShiftIdentityClaims.TokenPurpose, purpose));
        claims.AddRange(extra ?? []);
        var jwt = new JwtSecurityToken(issuer ?? settings.Issuer, audience ?? settings.Audience, claims,
            notBefore?.UtcDateTime, expires.UtcDateTime,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key ?? settings.Key)), algorithm));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
