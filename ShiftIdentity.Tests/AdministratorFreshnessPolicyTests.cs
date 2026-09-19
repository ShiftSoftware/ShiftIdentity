using Microsoft.Extensions.Options;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Policy")]
public sealed class AdministratorFreshnessPolicyTests
{
    [Theory]
    [InlineData(0, true, null)]
    [InlineData(5, true, null)]
    [InlineData(1199, true, null)]
    [InlineData(1200, true, AuthenticationFailure.ReauthenticationRequired)]
    [InlineData(1440, true, AuthenticationFailure.ReauthenticationRequired)]
    [InlineData(1440, false, null)]
    public void Approved_grace_has_an_exact_twenty_hour_boundary_and_exemptions_keep_ordinary_sessions(int minutes, bool sensitive, AuthenticationFailure? expected)
    {
        var (services, actor, session) = Context(minutes);
        var result = AccountSecurityService.ActorRefusal(services, actor, session, p => p.CanWrite(ShiftIdentityActions.Users), sensitive);
        Assert.Equal(expected, result?.Code);
    }

    [Theory]
    [InlineData("permission", AuthenticationFailure.ClientDenied)]
    [InlineData("version", AuthenticationFailure.StaleOperation)]
    [InlineData("policy", AuthenticationFailure.StaleOperation)]
    [InlineData("factor", AuthenticationFailure.StaleOperation)]
    [InlineData("inactive", AuthenticationFailure.AccountUnavailable)]
    [InlineData("deleted", AuthenticationFailure.AccountUnavailable)]
    public void Exemptions_and_expired_grace_never_bypass_current_checks(string change, AuthenticationFailure expected)
    {
        foreach (var sensitive in new[] { false, true })
        {
            var (services, actor, session) = Context(1440);
            if (change == "permission") actor.User.AccessTree = "{}";
            if (change == "version") actor.Security.SecurityVersion++;
            if (change == "policy") actor.Policy.Revision++;
            if (change == "factor") actor.Security.FactorGeneration++;
            if (change == "inactive") actor.User.IsActive = false;
            if (change == "deleted") actor.User.IsDeleted = true;
            Assert.Equal(expected, AccountSecurityService.ActorRefusal(services, actor, session, p => p.CanWrite(ShiftIdentityActions.Users), sensitive)?.Code);
        }
    }

    [Theory]
    [InlineData(119, true, null)]
    [InlineData(120, true, AuthenticationFailure.ReauthenticationRequired)]
    [InlineData(121, false, null)]
    public void A_two_minute_host_override_controls_the_boundary_without_changing_exemptions(int ageSeconds, bool sensitive, AuthenticationFailure? expected)
    {
        var (services, actor, session) = Context(0);
        services = services with { Options = services.Options with { AdministratorAuthenticationGraceSeconds = 120 } };
        session = session with { Proof = session.Proof with { AuthenticatedAt = services.Clock.GetUtcNow().AddSeconds(-ageSeconds) } };
        Assert.Equal(expected, AccountSecurityService.ActorRefusal(services, actor, session, p => p.CanWrite(ShiftIdentityActions.Users), sensitive)?.Code);
    }

    private static (IdentityAdmissionServices, IdentitySecurityTransaction, SignedInContext) Context(int minutes)
    {
        var now = DateTimeOffset.UtcNow;
        var ids = new HashIdService(Options.Create(new ShiftEntityOptions()));
        var services = new IdentityAdmissionServices(null!, new("client", "audience"), new("issuer", "refresh", [], [], []), new ControlledClock(now), ids, null!, null!);
        var actor = new IdentitySecurityTransaction(new User { ID = 42, IsActive = true, AccessTree = "{\"ShiftIdentityActions\":{\"Users\":[\"w\"]}}" },
            new UserSecurityState { UserID = 42 }, new AuthenticationPolicyState(), null, _ => { }, _ => { });
        var session = new SignedInContext(new(42, 1, 1, 1, true, now.AddMinutes(-minutes), "client", "audience", false, ids.Encode<UserDTO>(42)), now.AddMinutes(15));
        return (services, actor, session);
    }
}
