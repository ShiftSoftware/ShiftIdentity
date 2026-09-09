using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

public sealed partial class SecurityLinkSqlTests
{
    [Fact]
    public async Task Sender_is_awaited_after_commit_without_holding_the_account_lock()
    {
        fixture.DeliveryLimits = new(HandoffTimeoutMilliseconds: 2000, PublicPaddingMilliseconds: 0);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new ControlledSink((_, ct) => release.Task.WaitAsync(ct)); fixture.EmailSink = sink;
        using var host = new IdentityHttpHost(fixture);
        var request = Request(host, Email);
        var message = await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(request.IsCompleted);
        try
        {
            await using var db = fixture.CreateContext();
            // A second account admission succeeds while the sender is still waiting.
            var opID = await new SqlIdentitySecurityStore(db).AdmitAsync(fixture.UserID, message.ID,
                new("test-client", "test-api"), unit => Task.FromResult(unit.Operation!.ID), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(message.ID, opID);
            await AssertPassword(fixture.Password, 1, false);
        }
        finally { release.TrySetResult(); }
        Assert.IsType<SecurityDeliveryRequested>(await request);
        Assert.Equal(1, sink.Calls);
        Assert.IsType<SecurityLinkOpened>(await Open(host, message.Grant, message.Purpose));
        await using var read = fixture.CreateContext();
        Assert.Contains(await read.Set<AuthenticationAuditEvent>().ToArrayAsync(), x => x.Outcome == "SecurityDeliveryAccepted" && x.OperationID == message.ID);
        Assert.DoesNotContain(read.Model.GetEntityTypes(), x => x.GetTableName() == "SecurityOutbox");
        await read.Database.OpenConnectionAsync();
        await using var command = read.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name = 'SecurityOutbox'";
        Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
        Assert.DoesNotContain(host.Services.GetServices<IHostedService>(), x => x.GetType().Name.Contains("Delivery", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_uncertain_handoff_is_private_and_retry_requires_a_new_request(bool afterAcceptance)
    {
        var sink = Inbox; sink.FailDeliveries = !afterAcceptance; sink.FailAfterAccept = afterAcceptance;
        using var host = new IdentityHttpHost(fixture);
        var known = await host.Client.PostAsJsonAsync("/api/identity/v2/password-reset/request", new RequestSecurityEmail(Email));
        var unknown = await host.Client.PostAsJsonAsync("/api/identity/v2/password-reset/request", new RequestSecurityEmail("missing-user"));
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode); Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Equal(1, sink.DeliveryCalls);
        Guid failedID;
        await using (var db = fixture.CreateContext())
        {
            var op = Assert.Single(await db.Set<AuthenticationOperation>().ToArrayAsync()); failedID = op.ID;
            Assert.Equal(AuthenticationOperationState.Cancelled, op.State); Assert.Null(op.OutstandingLinkSlot); Assert.Empty(op.HandleDigest);
            Assert.Contains(await db.Set<AuthenticationAuditEvent>().ToArrayAsync(), x => x.Outcome == "SecurityDeliveryUnconfirmed");
        }
        await AssertPassword(fixture.Password, 1, false);
        if (afterAcceptance) Assert.IsType<AuthenticationRefused>(await Open(host, await Grant(host), AuthenticationOperationPurpose.PasswordResetEmail));
        else Assert.Empty(sink.Messages);
        sink.FailDeliveries = false; sink.FailAfterAccept = false;
        await Request(host, Email); Assert.Equal(1, sink.DeliveryCalls); // Cooldown also bounds failures.
        clock.Advance(TimeSpan.FromSeconds(60));
        await Task.Delay(200); Assert.Equal(1, sink.DeliveryCalls); // Advancing time never dispatches mail.
        await Request(host, Email); Assert.Equal(2, sink.DeliveryCalls);
        Assert.NotEqual(failedID, sink.Messages.First().ID);
        Assert.IsType<SecurityLinkOpened>(await Open(host, await Grant(host), AuthenticationOperationPurpose.PasswordResetEmail));
        Assert.Equal(2, (await State()).DeliveryCount);
    }

    [Fact]
    public async Task Missing_sender_is_a_global_failure_without_creating_a_grant()
    {
        fixture.EmailSink = null;
        using var host = new IdentityHttpHost(fixture);
        foreach (var identifier in new[] { Email, "missing-user" })
        {
            using var response = await host.Client.PostAsJsonAsync("/api/identity/v2/password-reset/request", new RequestSecurityEmail(identifier));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(AuthenticationFailure.Unavailable, Assert.IsType<AuthenticationRefused>(await IdentityHttpHost.Read(response)).Code);
        }
        var login = Assert.IsType<SessionIssued>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge));
        Assert.Equal(AuthenticationFailure.Unavailable, Assert.IsType<AuthenticationRefused>(await Post(host,
            "email-verification/request-current", new { }, login.Session.Token)).Code);
        await using var db = fixture.CreateContext(); Assert.Empty(await db.Set<AuthenticationOperation>().ToArrayAsync());
        Assert.Equal(0, (await State()).DeliveryCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sender_timeout_is_bounded_and_late_result_cannot_cancel_a_completed_retry(bool lateFailure)
    {
        var late = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var sink = new ControlledSink((_, _) => Interlocked.Increment(ref calls) == 1 ? late.Task : Task.CompletedTask);
        fixture.EmailSink = sink;
        using var host = new IdentityHttpHost(fixture);
        var timer = Stopwatch.StartNew();
        Assert.IsType<SecurityDeliveryRequested>(await Request(host, Email).WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(timer.Elapsed >= TimeSpan.FromMilliseconds(100));
        var first = sink.Messages.Single();
        Assert.IsType<AuthenticationRefused>(await Open(host, first.Grant, first.Purpose));
        clock.Advance(TimeSpan.FromSeconds(60)); await Request(host, Email);
        var next = sink.Messages.Last(); Assert.NotEqual(first.ID, next.ID);
        var page = Assert.IsType<SecurityLinkOpened>(await Open(host, next.Grant, next.Purpose));
        Assert.IsType<ReturnToLogin>(await Reset(host, page.PageHandle));
        if (lateFailure) late.TrySetException(new IOException("Synthetic late failure.")); else late.TrySetResult();
        await Task.Delay(50);
        await AssertPassword(NewPassword, 2, true); Assert.Equal(2, sink.Calls);
        await using var db = fixture.CreateContext();
        Assert.Equal(AuthenticationOperationState.Completed, (await db.Set<AuthenticationOperation>().SingleAsync(x => x.ID == next.ID)).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Request_cancellation_never_leaves_framework_mail_work(bool beforePersistence)
    {
        using var cancellation = new CancellationTokenSource();
        var sink = new ControlledSink((_, ct) => Task.Delay(Timeout.Infinite, ct)); fixture.EmailSink = sink;
        fixture.DeliveryLimits = new(HandoffTimeoutMilliseconds: 2000, PublicPaddingMilliseconds: 0);
        using var host = new IdentityHttpHost(fixture, observe: point =>
        { if (beforePersistence && point == "SecurityLinkPrepared") cancellation.Cancel(); });
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider.GetRequiredService<IdentityAdmissionServices>();
        var request = AccountSecurityService.RequestSecurityEmailAsync(services, new(Email), false, "cancel-test", cancellation.Token);
        if (!beforePersistence)
        { await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.Equal(beforePersistence ? 0 : 1, sink.Calls);
        await using var db = fixture.CreateContext();
        var operations = await db.Set<AuthenticationOperation>().ToArrayAsync();
        if (beforePersistence) Assert.Empty(operations);
        else Assert.Equal(AuthenticationOperationState.Cancelled, Assert.Single(operations).State);
        await AssertPassword(fixture.Password, 1, false);
    }

    [Theory]
    [InlineData("reset", false)]
    [InlineData("reset", true)]
    [InlineData("verify", false)]
    [InlineData("verify", true)]
    [InlineData("resend", false)]
    [InlineData("resend", true)]
    [InlineData("contact", false)]
    [InlineData("contact", true)]
    [InlineData("version", false)]
    [InlineData("version", true)]
    public async Task Changes_during_handoff_keep_current_authority_and_never_undo_completion(string change, bool fail)
    {
        fixture.DeliveryLimits = new(HandoffTimeoutMilliseconds: 3000, PublicPaddingMilliseconds: 0);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var sink = new ControlledSink((_, ct) => Interlocked.Increment(ref count) == 1 ? release.Task.WaitAsync(ct) : Task.CompletedTask);
        fixture.EmailSink = sink;
        using var host = new IdentityHttpHost(fixture);
        var request = Request(host, Email, change == "verify");
        var message = await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            if (change is "reset" or "verify")
            {
                var page = Assert.IsType<SecurityLinkOpened>(await Open(host, message.Grant, message.Purpose));
                if (change == "reset") Assert.IsType<ReturnToLogin>(await Reset(host, page.PageHandle));
                else Assert.IsType<EmailVerificationCompleted>(await Verify(host, page.PageHandle));
            }
            else if (change == "resend")
            {
                clock.Advance(TimeSpan.FromSeconds(60));
                // Use an independent service scope with short response padding, sharing the same store/sink.
                using var scope = host.Services.CreateScope();
                var services = scope.ServiceProvider.GetRequiredService<IdentityAdmissionServices>();
                await AccountSecurityService.RequestSecurityEmailAsync(services with { DeliveryLimits = new(HandoffTimeoutMilliseconds: 100, PublicPaddingMilliseconds: 0) }, new(Email), false, "retry", CancellationToken.None);
            }
            else
            {
                await using var db = fixture.CreateContext();
                await new SqlIdentitySecurityStore(db).AdmitAsync(fixture.UserID, null, new("test-client", "test-api"), unit =>
                {
                    if (change == "contact") RecoveryContact.ApplyAuthorizedEmailChange(unit, unit.Security.SecurityVersion,
                        unit.Security.ContactRevision, "changed@example.invalid", RecoveryEmailProvenance.TrustedAdminAssignment, clock.GetUtcNow());
                    else unit.Security.SecurityVersion++;
                    return Task.FromResult(true);
                }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
            }
        }
        finally { if (fail) release.TrySetException(new IOException("Synthetic handoff failure.")); else release.TrySetResult(); }
        Assert.IsType<SecurityDeliveryRequested>(await request);
        Assert.IsType<AuthenticationRefused>(await Open(host, message.Grant, message.Purpose));
        if (change == "reset") await AssertPassword(NewPassword, 2, true);
        if (change == "verify") await AssertPassword(fixture.Password, 1, true);
        if (change == "resend")
        {
            var next = sink.Messages.Last(); Assert.NotEqual(message.ID, next.ID);
            Assert.IsType<SecurityLinkOpened>(await Open(host, next.Grant, next.Purpose));
        }
        await using var read = fixture.CreateContext();
        var original = await read.Set<AuthenticationOperation>().SingleAsync(x => x.ID == message.ID);
        if (change is "reset" or "verify") Assert.Equal(AuthenticationOperationState.Completed, original.State);
        if (change == "resend") Assert.Equal(AuthenticationOperationState.Superseded, original.State);
    }

    [Fact]
    public async Task Public_response_floor_covers_both_fast_sender_and_unknown_account()
    {
        using var host = new IdentityHttpHost(fixture);
        foreach (var identifier in new[] { Email, "absent-user" })
        {
            var start = Stopwatch.StartNew(); await Request(host, identifier);
            Assert.True(start.Elapsed >= TimeSpan.FromMilliseconds(140));
        }
        Assert.Equal(1, Inbox.DeliveryCalls);
    }

    [Fact]
    public async Task Unconfirmed_grant_commit_never_calls_sender_and_retry_supersedes_safely()
    {
        using var fault = new IdentityHttpHost(fixture, null, null, new RequestCommitFault());
        Assert.IsType<SecurityDeliveryRequested>(await Request(fault, Email));
        Assert.Equal(0, Inbox.DeliveryCalls);
        await using (var db = fixture.CreateContext()) Assert.Single(await db.Set<AuthenticationOperation>().ToArrayAsync());
        clock.Advance(TimeSpan.FromSeconds(60));
        using var host = new IdentityHttpHost(fixture); await Request(host, Email);
        Assert.Equal(1, Inbox.DeliveryCalls);
        Assert.IsType<SecurityLinkOpened>(await Open(host, await Grant(host), AuthenticationOperationPurpose.PasswordResetEmail));
    }

    private sealed class ControlledSink(Func<SecurityEmail, CancellationToken, Task> action) : ISecurityEmailSink
    {
        private readonly ConcurrentQueue<SecurityEmail> attempts = new();
        public TaskCompletionSource<SecurityEmail> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SecurityEmail[] Messages => attempts.ToArray();
        public int Calls => attempts.Count;
        public Task DeliverAsync(SecurityEmail message, CancellationToken ct)
        { attempts.Enqueue(message); Entered.TrySetResult(message); return action(message, ct); }
    }
    private sealed class RequestCommitFault : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<AuthenticationOperation>().Any(x => x.Entity.State == AuthenticationOperationState.AwaitingExplicitSubmit) == true)
                throw new IOException("Synthetic unconfirmed grant commit.");
            return Task.CompletedTask;
        }
    }
}
