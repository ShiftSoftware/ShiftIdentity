using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class SecurityLinkConcurrencySqlTests : SecurityLinkTestBase, IClassFixture<SqlIdentityFixture>
{
    public SecurityLinkConcurrencySqlTests(SqlIdentityFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Contact_change_and_change_back_cannot_revive_a_prepared_password_reset()
    {
        using var host = new IdentityHttpHost(fixture); await Request(host, Email);
        var original = await Grant(host);
        var page = Assert.IsType<SecurityLinkOpened>(await Open(host, original, AuthenticationOperationPurpose.PasswordResetEmail));
        using var gate = new AdmissionGate("ResetPasswordPrepared");
        using var racing = new IdentityHttpHost(fixture, observe: gate.Observe);
        var reset = Task.Run(() => Reset(racing, page.PageHandle));
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await using var db = fixture.CreateContext();
            var store = new SqlIdentitySecurityStore(db);
            foreach (var email in new[] { "changed-" + Guid.NewGuid().ToString("N") + "@example.invalid", Email })
                await store.AdmitAsync(fixture.UserID, null, new("test-client", "test-api"), unit => Task.FromResult(
                    RecoveryContact.ApplyAuthorizedEmailChange(unit, unit.Security.SecurityVersion, unit.Security.ContactRevision,
                        email, RecoveryEmailProvenance.TrustedAdminAssignment, clock.GetUtcNow())), TestContext.Current.CancellationToken);
        }
        finally { gate.Release(); }
        Assert.IsType<AuthenticationRefused>(await reset);
        Assert.IsType<AuthenticationRefused>(await Open(host, original, AuthenticationOperationPurpose.PasswordResetEmail));
        await AssertPassword(fixture.Password, 3, false);
        Assert.Equal(3, (await State()).ContactRevision);
        await using var read = fixture.CreateContext();
        Assert.Null((await read.Set<AuthenticationOperation>().SingleAsync()).OutstandingLinkSlot);
    }

    [Fact]
    public async Task Concurrent_resends_share_cooldown_and_supersede_the_original_once()
    {
        using var first = new IdentityHttpHost(fixture); using var second = new IdentityHttpHost(fixture);
        await Request(first, Email); var old = await Grant(first);
        clock.Advance(TimeSpan.FromSeconds(60));
        var requests = await Task.WhenAll(Request(first, Email), Request(second, Email));
        Assert.All(requests, x => Assert.IsType<SecurityDeliveryRequested>(x));
        await using var db = fixture.CreateContext();
        Assert.Equal(2, Inbox.Messages.Length);
        Assert.Single(await db.Set<AuthenticationOperation>().Where(x => x.OutstandingLinkSlot != null).ToArrayAsync());
        Assert.Equal(2, (await State()).DeliveryCount);
        Assert.IsType<AuthenticationRefused>(await Open(first, old, AuthenticationOperationPurpose.PasswordResetEmail));
        Assert.IsType<SecurityLinkOpened>(await Open(first, await Grant(first), AuthenticationOperationPurpose.PasswordResetEmail));
    }

    [Fact]
    public async Task Reset_and_verification_share_hourly_budget_and_public_ingress_across_hosts()
    {
        // Only the counts matter here, so the twenty-three public requests take the shortest response floor.
        fixture.UseFastPublicResponses();
        using var host = new IdentityHttpHost(fixture); using var second = new IdentityHttpHost(fixture);
        for (var i = 0; i < 6; i++)
        {
            Assert.IsType<SecurityDeliveryRequested>(await Request(i % 2 == 0 ? host : second, Email, i % 2 == 0));
            clock.Advance(TimeSpan.FromSeconds(60));
        }
        Assert.Equal(5, Inbox.Messages.Length);
        // Unknown names consume the same ingress budget, without storing the raw names or address.
        for (var i = 0; i < 16; i++) await Request(host, "absent-" + i);
        await using (var db = fixture.CreateContext())
        {
            var bucket = Assert.Single(await db.Set<AuthThrottleBucket>().ToArrayAsync());
            Assert.Equal(20, bucket.Count); Assert.Equal(64, bucket.Key.Length);
        }
        clock.Advance(TimeSpan.FromHours(1));
        await Request(host, Email);
        Assert.Equal(1, (await State()).DeliveryCount);
    }

    [Fact]
    public async Task Two_completions_have_one_winner_and_one_version_increment()
    {
        using var host = new IdentityHttpHost(fixture); await Request(host, Email);
        var page = Assert.IsType<SecurityLinkOpened>(await Open(host, await Grant(host), AuthenticationOperationPurpose.PasswordResetEmail));
        using var other = new IdentityHttpHost(fixture);
        var results = await Task.WhenAll(Reset(host, page.PageHandle), Reset(other, page.PageHandle));
        Assert.Single(results.OfType<ReturnToLogin>()); Assert.Single(results.OfType<AuthenticationRefused>());
        await AssertPassword(NewPassword, 2, true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reset_rolls_back_password_version_verification_consumption_and_audit_when_save_fails(bool afterSave)
    {
        using var host = new IdentityHttpHost(fixture); await Request(host, Email);
        var page = Assert.IsType<SecurityLinkOpened>(await Open(host, await Grant(host), AuthenticationOperationPurpose.PasswordResetEmail));
        using var failing = new IdentityHttpHost(fixture, null, null, new CompletionSaveFault(afterSave));
        Assert.Equal(AuthenticationFailure.Unavailable, Assert.IsType<AuthenticationRefused>(await Reset(failing, page.PageHandle)).Code);
        await AssertPassword(fixture.Password, 1, false);
        await using (var db = fixture.CreateContext()) Assert.DoesNotContain(await db.Set<AuthenticationAuditEvent>().ToArrayAsync(), x => x.Outcome == "PasswordResetCompleted");
        Assert.IsType<ReturnToLogin>(await Reset(host, page.PageHandle));
    }

    [Fact]
    public async Task Resend_between_password_preparation_and_commit_refuses_the_old_reset()
    {
        using var host = new IdentityHttpHost(fixture); await Request(host, Email);
        var page = Assert.IsType<SecurityLinkOpened>(await Open(host, await Grant(host), AuthenticationOperationPurpose.PasswordResetEmail));
        using var gate = new AdmissionGate("ResetPasswordPrepared");
        using var racing = new IdentityHttpHost(fixture, observe: gate.Observe);
        var reset = Task.Run(() => Reset(racing, page.PageHandle));
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(60));
        await Request(host, Email);
        gate.Release();
        Assert.IsType<AuthenticationRefused>(await reset);
        await AssertPassword(fixture.Password, 1, false);
    }

    [Fact]
    public async Task A_password_reset_never_upgrades_stale_refresh_or_signed_in_authority()
    {
        using var host = new IdentityHttpHost(fixture);
        var old = Assert.IsType<SessionIssued>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge));
        await Request(host, Email);
        var page = Assert.IsType<SecurityLinkOpened>(await Open(host, await Grant(host), AuthenticationOperationPurpose.PasswordResetEmail));
        using var gate = new AdmissionGate("ResetPasswordMutation");
        using var racing = new IdentityHttpHost(fixture, observe: gate.Observe);
        var reset = Task.Run(() => Reset(racing, page.PageHandle));
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var refresh = host.RefreshAsync(old.Session.RefreshToken);
        gate.Release();
        Assert.IsType<ReturnToLogin>(await reset); Assert.IsType<AuthenticationRefused>(await refresh);
        Assert.IsType<AuthenticationRefused>(await host.StartPasswordChangeAsync(old.Session.Token, IdentityHttpHost.Pkce().Challenge));
    }

}
