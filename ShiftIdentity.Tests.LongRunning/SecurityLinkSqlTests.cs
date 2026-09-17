using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class SecurityLinkSqlTests : SecurityLinkTestBase, IClassFixture<SqlIdentityFixture>
{
    public SecurityLinkSqlTests(SqlIdentityFixture fixture) : base(fixture) { }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Emailed_reset_is_inert_until_atomic_submission_and_preserves_factor_state(bool mfa)
    {
        if (mfa)
        {
            await using var db = fixture.CreateContext();
            var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
            fixture.SetSyntheticFactor(state, fixture.FactorSecret);
            await db.SaveChangesAsync();
        }
        using var host = new IdentityHttpHost(fixture);
        var loginPkce = IdentityHttpHost.Pkce();
        var login = await host.LoginAsync(fixture, loginPkce.Challenge);
        var old = mfa ? Assert.IsType<SessionIssued>(await host.CompleteAsync(Assert.IsType<ChallengeRequired>(login).Challenge.Handle!, Code(), loginPkce.Verifier)) : Assert.IsType<SessionIssued>(login);
        Assert.IsType<SecurityDeliveryRequested>(await Request(host, fixture.Username));
        var grant = await Grant(host);
        var before = await State();
        var page = Assert.IsType<SecurityLinkOpened>(await Open(host, grant, AuthenticationOperationPurpose.PasswordResetEmail));
        Assert.DoesNotContain(Email, page.MaskedTarget);
        Assert.IsType<SecurityLinkOpened>(await Open(host, grant, AuthenticationOperationPurpose.PasswordResetEmail));
        await AssertPassword(fixture.Password, 1, false);
        Assert.IsType<ReturnToLogin>(await Reset(host, page.PageHandle));
        await AssertPassword(NewPassword, 2, true);
        var after = await State();
        Assert.Equal(before.FactorGeneration, after.FactorGeneration);
        Assert.Equal(before.ProtectedTotpSecret, after.ProtectedTotpSecret);
        Assert.Equal(before.LocalMfaRecoveryRequired, after.LocalMfaRecoveryRequired);
        Assert.IsType<AuthenticationRefused>(await Reset(host, page.PageHandle));
        Assert.IsType<AuthenticationRefused>(await Open(host, grant, AuthenticationOperationPurpose.PasswordResetEmail));
        Assert.IsType<AuthenticationRefused>(await host.RefreshAsync(old.Session.RefreshToken));
        using var resource = new HttpRequestMessage(HttpMethod.Get, "/fixture/resource");
        resource.Headers.Authorization = new("Bearer", old.Session.Token);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(resource)).StatusCode);
        clock.Advance(TimeSpan.FromMinutes(15));
        using var expired = new HttpRequestMessage(HttpMethod.Get, "/fixture/resource"); expired.Headers.Authorization = new("Bearer", old.Session.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(expired)).StatusCode);
    }

    [Fact]
    public async Task Unknown_provenance_cannot_reset_even_when_legacy_verified_flag_is_forged()
    {
        await Contact(Email, false, verified: true);
        using var host = new IdentityHttpHost(fixture);
        Assert.IsType<SecurityDeliveryRequested>(await Request(host, Email));
        await using var db = fixture.CreateContext();
        Assert.Empty(Inbox.Messages);
        Assert.False(RecoveryContact.IsEligible(await db.Users.SingleAsync(x => x.ID == fixture.UserID), await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID)));
    }

    [Fact]
    public async Task Explicit_verification_establishes_eligibility_without_version_or_session()
    {
        await Contact(Email, false);
        using var host = new IdentityHttpHost(fixture);
        Assert.IsType<SecurityDeliveryRequested>(await Request(host, "  " + Email.ToUpperInvariant() + "  ", true));
        var grant = await Grant(host);
        var page = Assert.IsType<SecurityLinkOpened>(await Open(host, grant, AuthenticationOperationPurpose.EmailVerify));
        await AssertPassword(fixture.Password, 1, false);
        Assert.IsType<AuthenticationRefused>(await Reset(host, page.PageHandle));
        Assert.IsType<EmailVerificationCompleted>(await Verify(host, page.PageHandle));
        await AssertPassword(fixture.Password, 1, true);
        Assert.Equal(RecoveryEmailProvenance.OwnershipVerification, (await State()).RecoveryEmailProvenance);
        Assert.IsType<AuthenticationRefused>(await Verify(host, page.PageHandle));
        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.IsType<SecurityDeliveryRequested>(await Request(host, Email));
        await using var db = fixture.CreateContext();
        Assert.Equal(2, Inbox.Messages.Length);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("ineligible")]
    [InlineData("deleted")]
    [InlineData("inactive")]
    [InlineData("alreadyVerified")]
    [InlineData("cooldown")]
    public async Task Public_suppression_has_identical_status_and_body(string scenario)
    {
        // Only the status and body are compared, so the fourteen public requests take the shortest response floor.
        fixture.UseFastPublicResponses();
        using var host = new IdentityHttpHost(fixture);
        var identifier = fixture.Username;
        var verification = scenario == "alreadyVerified";
        if (scenario == "unknown") identifier = "absent-" + Guid.NewGuid();
        if (scenario == "missing") await Contact(null, false);
        if (scenario == "ineligible") await Contact(Email, false);
        if (scenario == "alreadyVerified") await Contact(Email, true, true);
        if (scenario is "deleted" or "inactive")
        {
            await using var db = fixture.CreateContext();
            var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
            user.IsDeleted = scenario == "deleted"; user.IsActive = scenario != "inactive";
            await db.SaveChangesAsync();
        }
        if (scenario == "cooldown") await Request(host, identifier);
        using var response = await host.Client.PostAsJsonAsync("/api/identity/v2/" + (verification ? "email-verification" : "password-reset") + "/request", new RequestSecurityEmail(identifier));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("{\"kind\":\"deliveryRequested\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cross_field_ambiguity_suppresses_both_accounts_and_audits_without_identifier()
    {
        await fixture.CreateSyntheticUserAsync(Email);
        using var host = new IdentityHttpHost(fixture);
        Assert.IsType<SecurityDeliveryRequested>(await Request(host, Email));
        await using var db = fixture.CreateContext();
        Assert.Empty(Inbox.Messages);
        Assert.Contains(await db.Set<AuthenticationAuditEvent>().ToArrayAsync(), x => x.Outcome == "AmbiguousSecurityEmailRequest");
    }

    [Theory]
    [InlineData("destination")]
    [InlineData("revision")]
    [InlineData("version")]
    [InlineData("factor")]
    [InlineData("policy")]
    [InlineData("eligibility")]
    [InlineData("expiry")]
    [InlineData("pageExpiry")]
    public async Task Stale_links_cannot_mutate(string reason)
    {
        using var host = new IdentityHttpHost(fixture);
        await Request(host, Email);
        var grant = await Grant(host);
        var page = Assert.IsType<SecurityLinkOpened>(await Open(host, grant, AuthenticationOperationPurpose.PasswordResetEmail));
        await using (var db = fixture.CreateContext())
        {
            var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
            if (reason == "destination") (await db.Users.SingleAsync(x => x.ID == fixture.UserID)).Email = "other@example.invalid";
            if (reason == "revision") state.ContactRevision++;
            if (reason == "version") state.SecurityVersion++;
            if (reason == "factor") state.FactorGeneration++;
            if (reason == "policy") (await db.Set<AuthenticationPolicyState>().SingleAsync()).Revision++;
            if (reason == "eligibility") RecoveryContact.Invalidate(state);
            await db.SaveChangesAsync();
        }
        if (reason == "expiry") clock.Advance(TimeSpan.FromMinutes(30));
        if (reason == "pageExpiry") clock.Advance(TimeSpan.FromMinutes(10));
        var refused = Assert.IsType<AuthenticationRefused>(await Reset(host, page.PageHandle));
        Assert.Equal(AuthenticationFailure.InvalidGrant, refused.Code);
        await using var read = fixture.CreateContext();
        var user = await read.Users.SingleAsync(x => x.ID == fixture.UserID);
        Assert.True(HashService.VerifyVersionedPassword(fixture.Password, user.Salt, user.PasswordHash));
        Assert.False(user.EmailVerified);
    }

    [Fact]
    public async Task Link_and_page_purposes_clients_and_credentials_are_not_interchangeable()
    {
        using var host = new IdentityHttpHost(fixture);
        await Request(host, Email);
        var grant = await Grant(host);
        Assert.IsType<AuthenticationRefused>(await Open(host, grant, AuthenticationOperationPurpose.EmailVerify));
        Assert.IsType<AuthenticationRefused>(await Open(host, grant, AuthenticationOperationPurpose.MfaRecovery));
        Assert.IsType<AuthenticationRefused>(await Open(host, grant[..^1] + (grant[^1] == 'A' ? "B" : "A"), AuthenticationOperationPurpose.PasswordResetEmail));
        var page = Assert.IsType<SecurityLinkOpened>(await Open(host, grant, AuthenticationOperationPurpose.PasswordResetEmail));
        Assert.IsType<AuthenticationRefused>(await Verify(host, page.PageHandle));
        Assert.IsType<AuthenticationRefused>(await Reset(host, grant));
        using var other = new IdentityHttpHost(fixture, new("other-client", "other-api"));
        Assert.IsType<AuthenticationRefused>(await Open(other, grant, AuthenticationOperationPurpose.PasswordResetEmail));
        Assert.IsType<AuthenticationRefused>(await Reset(other, page.PageHandle));
        await AssertPassword(fixture.Password, 1, false);
    }

    [Fact]
    public async Task Password_policy_errors_keep_the_grant_usable()
    {
        using var host = new IdentityHttpHost(fixture); await Request(host, Email);
        var page = Assert.IsType<SecurityLinkOpened>(await Open(host, await Grant(host), AuthenticationOperationPurpose.PasswordResetEmail));
        Assert.Equal(AuthenticationFailure.InvalidNewPassword, Assert.IsType<AuthenticationRefused>(await Reset(host, page.PageHandle, "short")).Code);
        Assert.Equal(PasswordPolicyFailure.SameAsCurrent, Assert.IsType<AuthenticationRefused>(await Reset(host, page.PageHandle, fixture.Password)).PasswordFailure);
        await AssertPassword(fixture.Password, 1, false);
        Assert.IsType<ReturnToLogin>(await Reset(host, page.PageHandle));
    }

    [Fact]
    public async Task GET_HEAD_and_prefetch_never_consume_or_verify()
    {
        using var host = new IdentityHttpHost(fixture); await Request(host, Email, true);
        var grant = await Grant(host);
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Head })
        foreach (var route in new[] { "/api/identity/v2/security-link/open", "/api/identity/v2/email-verification/complete", "/api/identity/v2/password-reset/complete" })
        {
            using var request = new HttpRequestMessage(method, route + "?grant=" + grant);
            request.Headers.Add("Purpose", "prefetch");
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await host.Client.SendAsync(request)).StatusCode);
        }
        await AssertPassword(fixture.Password, 1, false);
        Assert.IsType<SecurityLinkOpened>(await Open(host, grant, AuthenticationOperationPurpose.EmailVerify));
    }
}
