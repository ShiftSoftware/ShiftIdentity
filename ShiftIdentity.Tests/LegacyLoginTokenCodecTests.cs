using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Policy")]
public sealed class LegacyLoginTokenCodecTests
{
    private readonly ControlledClock clock = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    private readonly IdentityAdmissionServices services;
    private readonly string verifier = IdentityHttpHost.Pkce().Verifier;

    public LegacyLoginTokenCodecTests()
    {
        using var rsa = RSA.Create(2048);
        var options = new IdentityAdmissionOptions("https://identity.invalid", "refresh", rsa.ExportRSAPrivateKey(),
            RandomNumberGenerator.GetBytes(64), RandomNumberGenerator.GetBytes(32));
        services = new(null!, new("test-client", "test-api"), options, clock,
            new HashIdService(Options.Create(new ShiftEntityOptions())), null!, new(options, clock));
    }

    [Theory]
    [InlineData(AuthenticationStep.ExistingMfa, AuthenticationOperationPurpose.Login, AuthPurpose.Mfa)]
    [InlineData(AuthenticationStep.PasswordChange, AuthenticationOperationPurpose.PasswordChange, AuthPurpose.ChangePassword)]
    [InlineData(AuthenticationStep.NewMfa, AuthenticationOperationPurpose.MfaEnrollment, AuthPurpose.MfaEnrollment)]
    [InlineData(AuthenticationStep.ExistingMfa, AuthenticationOperationPurpose.PasswordChange, AuthPurpose.Mfa)]
    public void Purpose_envelopes_preserve_bearer_claims_without_session_proof(AuthenticationStep step,
        AuthenticationOperationPurpose purpose, AuthPurpose flow)
    {
        var token = Issue(step, purpose);
        var proof = new LegacyLoginTokenCodec(services).Read("Bearer " + token.Token)!;
        Assert.Equal(verifier, proof.Verifier); Assert.Equal(purpose, proof.Purpose); Assert.Equal(flow, proof.Flow);
        Assert.Equal("Synthetic", proof.Principal.FindFirstValue(ClaimTypes.GivenName));
        Assert.Null(token.RefreshToken); Assert.Null(token.RefreshTokenLifeTimeInSeconds);
        Assert.Equal(300, token.TokenLifeTimeInSeconds);
        Assert.Null(services.Tokens.ValidateAccess(token.Token, services.Client));
        Assert.Null(services.Tokens.ValidateRefresh(token.Token, services.Client));
        Assert.DoesNotContain(proof.Principal.Claims, x => x.Type is "auth_time" or "shift_mfa" or "shift_sv");
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("wrong-key")]
    [InlineData("wrong-client")]
    [InlineData("wrong-issuer")]
    [InlineData("purpose")]
    [InlineData("handle")]
    [InlineData("verifier")]
    [InlineData("duplicate")]
    [InlineData("algorithm")]
    [InlineData("missing-expiry")]
    [InlineData("future")]
    [InlineData("oversized")]
    public void Malformed_or_wrong_context_steps_are_refused(string scenario)
    {
        var token = Issue(AuthenticationStep.ExistingMfa, AuthenticationOperationPurpose.Login).Token;
        var reader = services;
        if (scenario == "expired") clock.Advance(TimeSpan.FromMinutes(5));
        else if (scenario == "wrong-key") reader = services with { Options = services.Options with { OperationKey = RandomNumberGenerator.GetBytes(32) } };
        else if (scenario == "wrong-client") reader = services with { Client = new("other", "test-api") };
        else if (scenario == "wrong-issuer") reader = services with { Options = services.Options with { Issuer = "https://other.invalid" } };
        else if (scenario == "oversized") token = new string('a', 8193);
        else
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            if (scenario == "purpose") jwt.Payload[ShiftIdentityClaims.TokenPurpose] = AuthPurpose.ChangePassword.ToString();
            if (scenario == "handle") jwt.Payload["shift_login_handle"] = "bad";
            if (scenario == "verifier") jwt.Payload["shift_login_verifier"] = "short";
            if (scenario == "missing-expiry") jwt.Payload.Remove("exp");
            if (scenario == "future") jwt.Payload["nbf"] = clock.GetUtcNow().AddSeconds(1).ToUnixTimeSeconds();
            var payload = jwt.Payload.SerializeToJson();
            if (scenario == "duplicate") payload = payload[..^1] + ",\"exp\":9999999999}";
            var header = scenario == "algorithm" ? "{\"alg\":\"none\"}" : jwt.Header.SerializeToJson();
            var unsigned = Base64UrlEncoder.Encode(header) + "." + Base64UrlEncoder.Encode(payload);
            var key = HMACSHA256.HashData(services.Options.OperationKey, Encoding.UTF8.GetBytes("ShiftIdentity.LegacyLoginStep.v1"));
            token = unsigned + "." + Base64UrlEncoder.Encode(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(unsigned)));
        }
        Assert.Null(new LegacyLoginTokenCodec(reader).Read("Bearer " + token));
    }

    private ShiftSoftware.ShiftIdentity.Core.DTOs.TokenDTO Issue(AuthenticationStep step, AuthenticationOperationPurpose purpose)
    {
        var handle = OperationCredential.Create(services.Options.OperationKey);
        var unit = new IdentitySecurityTransaction(new() { ID = 42, Username = "synthetic", FullName = "Synthetic" }, new(), new(), null, _ => { }, _ => { });
        return new LegacyLoginTokenCodec(services).Issue(unit, new(step, handle.Handle, clock.GetUtcNow().AddMinutes(5), purpose), verifier);
    }
}
