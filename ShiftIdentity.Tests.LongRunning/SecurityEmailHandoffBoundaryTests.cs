using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class SecurityEmailHandoffBoundaryTests : SecurityLinkTestBase, IClassFixture<SqlIdentityFixture>
{
    public SecurityEmailHandoffBoundaryTests(SqlIdentityFixture fixture) : base(fixture) { }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accepted_handoff_survives_result_persistence_failure(bool afterSave)
    {
        var fault = new HandoffResultPersistenceFault(afterSave);
        using var host = new IdentityHttpHost(fixture, null, null, fault);
        var login = Assert.IsType<SessionIssued>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge));

        // This authenticated path exposes sender failure, so public response masking cannot hide a regression.
        Assert.IsType<SecurityDeliveryRequested>(await Post(host, "email-verification/request-current", new { }, login.Session.Token));

        Assert.Equal(1, fault.Calls);
        var message = Assert.Single(Inbox.Messages);
        Assert.Equal(1, Inbox.DeliveryCalls);
        await using (var db = fixture.CreateContext())
        {
            var op = await db.Set<AuthenticationOperation>().SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(message.ID, op.ID);
            Assert.Equal(AuthenticationOperationState.AwaitingExplicitSubmit, op.State);
            Assert.NotEmpty(op.HandleDigest);
            Assert.Null(op.CompletedAt);
            Assert.DoesNotContain(await db.Set<AuthenticationAuditEvent>().ToArrayAsync(TestContext.Current.CancellationToken),
                x => x.Outcome is "SecurityDeliveryAccepted" or "SecurityDeliveryUnconfirmed");
        }
        var page = Assert.IsType<SecurityLinkOpened>(await Open(host, await Grant(host), AuthenticationOperationPurpose.EmailVerify));
        Assert.IsType<EmailVerificationCompleted>(await Verify(host, page.PageHandle));
        await AssertPassword(fixture.Password, 1, true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unconfirmed_handoff_with_failed_result_persistence_remains_bound_until_explicit_retry(bool afterSave)
    {
        Inbox.FailAfterAccept = true;
        var fault = new HandoffResultPersistenceFault(afterSave);
        using var host = new IdentityHttpHost(fixture, null, null, fault);
        var login = Assert.IsType<SessionIssued>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge));

        Assert.Equal(AuthenticationFailure.Unavailable, Assert.IsType<AuthenticationRefused>(
            await Post(host, "email-verification/request-current", new { }, login.Session.Token)).Code);

        Assert.Equal(1, fault.Calls);
        var message = Assert.Single(Inbox.Messages);
        var originalGrant = await Grant(host);
        DateTimeOffset originalExpiry;
        await using (var db = fixture.CreateContext())
        {
            var op = await db.Set<AuthenticationOperation>().SingleAsync(TestContext.Current.CancellationToken);
            originalExpiry = op.ExpiresAt;
            Assert.Equal(message.ID, op.ID);
            Assert.Equal(AuthenticationOperationState.AwaitingExplicitSubmit, op.State);
            Assert.Equal(fixture.UserID, op.UserID);
            Assert.Equal(AuthenticationOperationPurpose.EmailVerify, op.Purpose);
            Assert.Equal(Email, op.Destination);
            Assert.Equal(1, op.SecurityVersion);
            Assert.Equal(1, op.ContactRevision);
            Assert.Equal(1, op.FactorGeneration);
            Assert.Equal(1, op.PolicyRevision);
            Assert.Equal("test-client", op.ClientID);
            Assert.Equal("test-api", op.Audience);
            Assert.False(op.External);
            Assert.Equal(clock.GetUtcNow().AddHours(24), op.ExpiresAt);
            Assert.NotEmpty(op.HandleDigest);
            Assert.NotNull(op.OutstandingLinkSlot);
            Assert.Null(op.CompletedAt);
            Assert.DoesNotContain(await db.Set<AuthenticationAuditEvent>().ToArrayAsync(TestContext.Current.CancellationToken),
                x => x.Outcome == "SecurityDeliveryUnconfirmed");
        }
        Assert.IsType<SecurityLinkOpened>(await Open(host, originalGrant, AuthenticationOperationPurpose.EmailVerify));
        await AssertPassword(fixture.Password, 1, false);

        Inbox.FailAfterAccept = false;
        Assert.IsType<SecurityDeliveryRequested>(await Post(host, "email-verification/request-current", new { }, login.Session.Token));
        Assert.Equal(1, Inbox.DeliveryCalls); // A failed result write does not refund the issuance cooldown.
        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(1, Inbox.DeliveryCalls);
        Assert.IsType<SecurityDeliveryRequested>(await Post(host, "email-verification/request-current", new { }, login.Session.Token));

        Assert.Equal(2, Inbox.DeliveryCalls);
        Assert.NotEqual(message.ID, Inbox.Messages.First().ID);
        Assert.IsType<AuthenticationRefused>(await Open(host, originalGrant, AuthenticationOperationPurpose.EmailVerify));
        Assert.IsType<SecurityLinkOpened>(await Open(host, await Grant(host), AuthenticationOperationPurpose.EmailVerify));
        await using var read = fixture.CreateContext();
        var original = await read.Set<AuthenticationOperation>().SingleAsync(x => x.ID == message.ID, TestContext.Current.CancellationToken);
        Assert.Equal(AuthenticationOperationState.Superseded, original.State);
        Assert.Equal(originalExpiry, original.ExpiresAt);
        Assert.Empty(original.HandleDigest);
        Assert.Null(original.OutstandingLinkSlot);
        Assert.Null(original.Destination);
        Assert.Equal(2, (await State()).DeliveryCount);
    }
}

/// <summary>Fails or blocks the write that records the handoff result; shared with the timing class.</summary>
internal sealed class HandoffResultPersistenceFault(bool afterSave = false, bool blockUntilCancellation = false) : SaveChangesInterceptor
{
    public int Calls { get; private set; }
    public bool CancellationObserved { get; private set; }

    private async Task FailAsync(DbContext? db, bool after, CancellationToken cancellationToken)
    {
        if (after != afterSave || db?.ChangeTracker.Entries<AuthenticationAuditEvent>().Any(x =>
                x.Entity.Outcome is "SecurityDeliveryAccepted" or "SecurityDeliveryUnconfirmed") != true) return;
        Calls++;
        if (blockUntilCancellation)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { CancellationObserved = true; throw; }
        }
        throw new IOException("Synthetic handoff result persistence failure.");
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { await FailAsync(eventData.Context, false, cancellationToken); return result; }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    { await FailAsync(eventData.Context, true, cancellationToken); return result; }
}
