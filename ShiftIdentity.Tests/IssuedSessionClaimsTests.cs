using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// What a deployed resource server sees in a staged session. Every consumer validates with the framework's bearer
/// registration, whose default inbound mapping turns <c>sub</c> into the name-identifier claim, so the access token
/// must carry the subject once: a second copy (the deployed profile route reads it with Single) is a breaking change
/// for a consumer that never redeployed.
/// </summary>
[Trait("Category", "Policy")]
public sealed class IssuedSessionClaimsTests
{
    private readonly ControlledClock clock = new(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
    private readonly AuthenticationClient client = new("test-client", "test-api");
    private readonly IdentityAdmissionOptions options;

    public IssuedSessionClaimsTests()
    {
        using var rsa = RSA.Create(2048);
        options = new("https://identity.invalid", "identity-refresh", rsa.ExportRSAPrivateKey(),
            RandomNumberGenerator.GetBytes(64), RandomNumberGenerator.GetBytes(32));
    }

    [Theory]
    [InlineData("classic")]
    [InlineData("json-web-token")]
    public async Task An_issued_session_yields_one_name_identifier_under_default_inbound_mapping(string handler)
    {
        var hashIds = new HashIdService(Options.Create(new ShiftEntityOptions()));
        var services = new IdentityAdmissionServices(null!, client, options, clock, hashIds, null!, new AdmissionTokenCodec(options, clock));
        var user = new User { ID = 42, Username = "synthetic", FullName = "Synthetic User", IsActive = true, Email = "person@example.invalid", Phone = "+12025550123",
            RegionID = 1, CompanyID = 2, CompanyBranchID = 3 };
        var unit = new IdentitySecurityTransaction(user, new UserSecurityState { UserID = 42 }, new AuthenticationPolicyState(), null, _ => { }, _ => { });
        var now = clock.GetUtcNow();
        var session = Assert.IsType<SessionIssued>(AdmissionRules.Issue(services, unit, AdmissionRules.Proof(unit, services, false, now), now)).Session;
        var subject = hashIds.Encode<UserDTO>(42);

        using var rsa = RSA.Create();
        rsa.ImportRSAPrivateKey(options.AccessPrivateKey, out _);
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = options.Issuer, ValidAudience = client.Audience, IssuerSigningKey = new RsaSecurityKey(rsa.ExportParameters(false)),
            ValidateIssuerSigningKey = true, ValidateLifetime = false
        };
        // The two handlers a consumer may validate with, each mapping inbound claims as the framework's bearer registration does.
        ClaimsPrincipal principal;
        if (handler == "classic") principal = new JwtSecurityTokenHandler().ValidateToken(session.Token, parameters, out _);
        else
        {
            var result = await new JsonWebTokenHandler { MapInboundClaims = true }.ValidateTokenAsync(session.Token, parameters);
            Assert.True(result.IsValid, result.Exception?.Message);
            principal = new ClaimsPrincipal(result.ClaimsIdentity);
        }
        Assert.Equal(subject, Assert.Single(principal.FindAll(ClaimTypes.NameIdentifier)).Value);
        Assert.Equal("synthetic", Assert.Single(principal.FindAll(ClaimTypes.Name)).Value);
        Assert.Equal("Synthetic User", Assert.Single(principal.FindAll(ClaimTypes.GivenName)).Value);
        Assert.Equal("person@example.invalid", Assert.Single(principal.FindAll(ClaimTypes.Email)).Value);
        Assert.Equal("false", Assert.Single(principal.FindAll(ShiftSoftware.ShiftIdentity.Core.ShiftIdentityClaims.ExternalToken)).Value);
        Assert.Empty(principal.FindAll("sub").Where(x => x.Value != subject));
        // The staged validator still reads the subject it needs.
        Assert.Equal(subject, services.Tokens.ValidateAccess(session.Token, client)!.Proof.Subject);
    }

    /// <summary>
    /// Region, company and branch drive data-level access downstream, and the application never saves a user without
    /// them. As with the legacy token, a missing one fails the issue (the admission transaction then rolls back)
    /// instead of yielding a session without that claim. The country is optional; the branch's city is not.
    /// </summary>
    [Theory]
    [InlineData("region")]
    [InlineData("company")]
    [InlineData("branch")]
    [InlineData("branch city")]
    public void A_user_without_a_region_company_or_branch_gets_no_session(string missing)
    {
        var services = new IdentityAdmissionServices(null!, client, options, clock,
            new HashIdService(Options.Create(new ShiftEntityOptions())), null!, new AdmissionTokenCodec(options, clock));
        var user = new User { ID = 42, Username = "synthetic", FullName = "Synthetic User", IsActive = true,
            RegionID = 1, CompanyID = 2, CompanyBranchID = 3, CompanyBranch = new CompanyBranch { CityID = 4 } };
        var now = clock.GetUtcNow();
        AuthOutcome IssueFor(User subject)
        {
            var unit = new IdentitySecurityTransaction(subject, new UserSecurityState { UserID = 42 }, new AuthenticationPolicyState(), null, _ => { }, _ => { });
            return AdmissionRules.Issue(services, unit, AdmissionRules.Proof(unit, services, false, now), now);
        }

        Assert.IsType<SessionIssued>(IssueFor(user));
        switch (missing)
        {
            case "region": user.RegionID = null; break;
            case "company": user.CompanyID = null; break;
            case "branch": user.CompanyBranchID = null; break;
            default: user.CompanyBranch.CityID = null; break;
        }
        Assert.Throws<InvalidOperationException>(() => IssueFor(user));
    }
}
