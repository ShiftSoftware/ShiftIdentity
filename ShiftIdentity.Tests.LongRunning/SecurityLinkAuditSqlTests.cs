using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// A refused reset or verification request still answers "delivery requested", but it now leaves one audit row that
/// names the reason. Support can then tell "never requested" from "requested but refused". The public answer is
/// checked in every case, and no row may hold the submitted identifier.
/// </summary>
[Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class SecurityLinkAuditSqlTests : SecurityLinkTestBase, IClassFixture<SqlIdentityFixture>
{
    public SecurityLinkAuditSqlTests(SqlIdentityFixture fixture) : base(fixture) { }

    [Theory]
    [InlineData("inactive", false, "PasswordResetAccountInactive")]
    [InlineData("deleted", false, "PasswordResetAccountInactive")]
    [InlineData("inactive", true, "EmailVerificationAccountInactive")]
    [InlineData("unknownProvenance", false, "PasswordResetIneligible")]
    [InlineData("missingEmail", false, "PasswordResetInvalidAddress")]
    [InlineData("missingEmail", true, "EmailVerificationInvalidAddress")]
    [InlineData("alreadyVerified", true, "EmailVerificationAlreadyVerified")]
    [InlineData("contactMismatch", false, "PasswordResetContactMismatch")]
    [InlineData("policy", false, "PasswordResetPolicyMismatch")]
    [InlineData("policy", true, "EmailVerificationPolicyMismatch")]
    public async Task A_refused_request_answers_the_same_and_audits_its_reason(string scenario, bool verification, string outcome)
    {
        fixture.UseFastPublicResponses();
        await using (var db = fixture.CreateContext())
        {
            var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
            var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
            if (scenario == "inactive") user.IsActive = false;
            if (scenario == "deleted") user.IsDeleted = true;
            if (scenario == "contactMismatch") state.UsernameLookupKey = RecoveryContact.Key("someone-else-" + Guid.NewGuid().ToString("N"));
            if (scenario == "policy") (await db.Set<AuthenticationPolicyState>().SingleAsync()).Revision++;
            await db.SaveChangesAsync();
        }
        // The legacy case that was silent in production: an unverified address whose origin is unknown.
        if (scenario == "unknownProvenance") await Contact(Email, false);
        if (scenario == "missingEmail") await Contact(null, false);
        if (scenario == "alreadyVerified") await Contact(Email, true, verified: true);
        // The contact mismatch keeps the email lookup key, so the request is made by email.
        var identifier = scenario is "contactMismatch" or "unknownProvenance" ? Email : fixture.Username;

        await AssertPublicAnswer(identifier, verification);

        Assert.Empty(Inbox.Messages);
        var audit = Assert.Single(await Audits(), x => x.Outcome == outcome);
        Assert.Equal(fixture.UserID, audit.UserID);
        Assert.Null(audit.OperationID);
        Assert.Null(audit.ActorUserID);
        await AssertNoIdentifierStored(identifier);
    }

    [Theory]
    [InlineData(false, "PasswordResetCooldown")]
    [InlineData(true, "EmailVerificationCooldown")]
    public async Task A_request_inside_the_cooldown_is_audited(bool verification, string outcome)
    {
        if (verification) await Contact(Email, false);
        using var host = new IdentityHttpHost(fixture);
        Assert.IsType<SecurityDeliveryRequested>(await Request(host, fixture.Username, verification));
        Assert.IsType<SecurityDeliveryRequested>(await Request(host, fixture.Username, verification));

        Assert.Single(Inbox.Messages);
        var audits = await Audits();
        Assert.Single(audits, x => x.Outcome == (verification ? "EmailVerificationRequested" : "PasswordResetRequested"));
        Assert.Equal(fixture.UserID, Assert.Single(audits, x => x.Outcome == outcome).UserID);
    }

    [Fact]
    public async Task A_request_over_the_hourly_limit_is_audited()
    {
        using var host = new IdentityHttpHost(fixture);
        for (var i = 0; i < 5; i++)
        {
            Assert.IsType<SecurityDeliveryRequested>(await Request(host, fixture.Username));
            clock.Advance(TimeSpan.FromSeconds(61));
        }
        Assert.IsType<SecurityDeliveryRequested>(await Request(host, fixture.Username));

        Assert.Equal(5, Inbox.Messages.Length);
        var audits = await Audits();
        Assert.Equal(fixture.UserID, Assert.Single(audits, x => x.Outcome == "PasswordResetHourlyLimit").UserID);
        Assert.DoesNotContain(audits, x => x.Outcome == "PasswordResetCooldown");
    }

    [Fact]
    public async Task The_per_address_limit_is_audited_once_per_window_without_a_user()
    {
        fixture.DeliveryLimits = SqlIdentityFixture.FastPublicResponses with { PublicPerIpPer15Minutes = 1 };
        using var host = new IdentityHttpHost(fixture);
        Assert.IsType<SecurityDeliveryRequested>(await Request(host, fixture.Username));
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.IsType<SecurityDeliveryRequested>(await Request(host, fixture.Username));
        Assert.IsType<SecurityDeliveryRequested>(await Request(host, fixture.Username, verification: true));

        Assert.Single(Inbox.Messages);
        var limited = Assert.Single(await Audits(), x => x.Outcome.EndsWith("IngressLimited"));
        // The first refusal in the window writes the row; later refusals from the same address write nothing more.
        Assert.Equal("PasswordResetIngressLimited", limited.Outcome);
        Assert.Equal(0, limited.UserID);
        Assert.Null(limited.VerificationReference);

        clock.Advance(TimeSpan.FromMinutes(15));
        Assert.IsType<SecurityDeliveryRequested>(await Request(host, fixture.Username));
        Assert.Equal(2, Inbox.Messages.Length);
    }

    [Fact]
    public async Task An_unknown_identifier_writes_no_row()
    {
        fixture.UseFastPublicResponses();
        var identifier = "absent-" + Guid.NewGuid().ToString("N");
        await AssertPublicAnswer(identifier, verification: false);
        Assert.Empty(await Audits());
    }

    [Fact]
    public async Task An_administrator_s_request_for_an_inactive_account_is_audited_with_the_administrator()
    {
        var admin = "audit-operator-" + Guid.NewGuid().ToString("N");
        var adminID = await fixture.CreateSyntheticUserAsync(admin, "{\"ShiftIdentityActions\":{\"Users\":[\"w\"]}}");
        await using (var db = fixture.CreateContext())
        {
            (await db.Users.SingleAsync(x => x.ID == fixture.UserID)).IsActive = false;
            await db.SaveChangesAsync();
        }
        using var host = new IdentityHttpHost(fixture);
        var access = Assert.IsType<SessionIssued>(await Post(host, "login",
            new PasswordLoginRequest(admin, fixture.Password, IdentityHttpHost.Pkce().Challenge))).Session.Token;
        Assert.IsType<SecurityDeliveryRequested>(await Post(host, "password-reset/admin", new AdminPasswordResetRequest(fixture.UserID), access));

        Assert.Empty(Inbox.Messages);
        var audit = Assert.Single(await Audits(), x => x.Outcome == "PasswordResetAccountInactive");
        Assert.Equal(fixture.UserID, audit.UserID);
        Assert.Equal(adminID, audit.ActorUserID);
    }

    private async Task AssertPublicAnswer(string identifier, bool verification)
    {
        using var host = new IdentityHttpHost(fixture);
        using var response = await host.Client.PostAsJsonAsync("/api/identity/v2/" + (verification ? "email-verification" : "password-reset") + "/request",
            new RequestSecurityEmail(identifier));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("{\"kind\":\"deliveryRequested\"}", await response.Content.ReadAsStringAsync());
    }

    private async Task<AuthenticationAuditEvent[]> Audits()
    {
        await using var db = fixture.CreateContext();
        return await db.Set<AuthenticationAuditEvent>().AsNoTracking().ToArrayAsync();
    }

    private async Task AssertNoIdentifierStored(string identifier)
    {
        foreach (var audit in await Audits())
        {
            Assert.DoesNotContain(identifier, audit.Outcome, StringComparison.OrdinalIgnoreCase);
            Assert.True(audit.VerificationReference is null || !audit.VerificationReference.Contains(identifier, StringComparison.OrdinalIgnoreCase));
        }
    }
}
