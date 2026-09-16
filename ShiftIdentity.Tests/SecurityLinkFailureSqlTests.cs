using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class SecurityLinkFailureSqlTests : SecurityLinkTestBase, IClassFixture<SqlIdentityFixture>
{
    public SecurityLinkFailureSqlTests(SqlIdentityFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Known_and_unknown_public_requests_remain_identical_when_loaded_policy_is_stale()
    {
        await using (var db = fixture.CreateContext())
        { var policy = await db.Set<AuthenticationPolicyState>().SingleAsync(); policy.Revision++; await db.SaveChangesAsync(); }
        using var host = new IdentityHttpHost(fixture);
        Assert.IsType<SecurityDeliveryRequested>(await Request(host, Email));
        Assert.IsType<SecurityDeliveryRequested>(await Request(host, "absent-account"));
        Assert.Empty(Inbox.Messages);
    }

    [Fact]
    public async Task Legacy_verified_flag_does_not_prevent_explicit_ownership_verification()
    {
        await Contact(Email, false, verified: true);
        using var host = new IdentityHttpHost(fixture); await Request(host, Email, true);
        var page = Assert.IsType<SecurityLinkOpened>(await Open(host, await Grant(host), AuthenticationOperationPurpose.EmailVerify));
        Assert.IsType<EmailVerificationCompleted>(await Verify(host, page.PageHandle));
        Assert.Equal(RecoveryEmailProvenance.OwnershipVerification, (await State()).RecoveryEmailProvenance);
        await AssertPassword(fixture.Password, 1, true);
    }

    [Fact]
    public async Task Invalid_password_submissions_lock_only_the_grant_after_five_attempts()
    {
        using var host = new IdentityHttpHost(fixture); await Request(host, Email);
        var page = Assert.IsType<SecurityLinkOpened>(await Open(host, await Grant(host), AuthenticationOperationPurpose.PasswordResetEmail));
        for (var i = 0; i < 5; i++)
            Assert.Equal(AuthenticationFailure.InvalidNewPassword, Assert.IsType<AuthenticationRefused>(await Reset(host, page.PageHandle, "short")).Code);
        Assert.Equal(AuthenticationFailure.InvalidGrant, Assert.IsType<AuthenticationRefused>(await Reset(host, page.PageHandle)).Code);
        await AssertPassword(fixture.Password, 1, false); Assert.Equal(0, (await State()).FailedProofs);
        Assert.IsType<SessionIssued>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge));
    }

    [Fact]
    public async Task Invalid_link_credentials_share_SQL_ingress_limit_across_hosts()
    {
        using var host = new IdentityHttpHost(fixture); using var other = new IdentityHttpHost(fixture);
        for (var i = 0; i < 60; i++)
            Assert.Equal(AuthenticationFailure.InvalidGrant, Assert.IsType<AuthenticationRefused>(await Open(i % 2 == 0 ? host : other, "invalid", AuthenticationOperationPurpose.EmailVerify)).Code);
        Assert.Equal(AuthenticationFailure.AttemptsExhausted, Assert.IsType<AuthenticationRefused>(await Open(other, "invalid", AuthenticationOperationPurpose.EmailVerify)).Code);
        clock.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(AuthenticationFailure.InvalidGrant, Assert.IsType<AuthenticationRefused>(await Open(other, "invalid", AuthenticationOperationPurpose.EmailVerify)).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Grant_and_delivery_budget_roll_back_before_any_sender_call(bool afterSave)
    {
        using var fault = new IdentityHttpHost(fixture, null, null, new RequestSaveFault(afterSave));
        Assert.IsType<SecurityDeliveryRequested>(await Request(fault, Email));
        await using (var db = fixture.CreateContext())
        {
            Assert.Empty(Inbox.Messages); Assert.Equal(0, Inbox.DeliveryCalls);
            Assert.Empty(await db.Set<AuthenticationOperation>().ToArrayAsync());
            Assert.Equal(0, (await State()).DeliveryCount);
        }
        using var host = new IdentityHttpHost(fixture);
        Assert.IsType<SecurityDeliveryRequested>(await Request(host, Email));
        Assert.IsType<SecurityLinkOpened>(await Open(host, await Grant(host), AuthenticationOperationPurpose.PasswordResetEmail));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dedicated_verification_rolls_back_flag_provenance_and_consumption(bool afterSave)
    {
        await Contact(Email, false);
        using var host = new IdentityHttpHost(fixture); await Request(host, Email, true);
        var page = Assert.IsType<SecurityLinkOpened>(await Open(host, await Grant(host), AuthenticationOperationPurpose.EmailVerify));
        using var fault = new IdentityHttpHost(fixture, null, null, new CompletionSaveFault(afterSave));
        Assert.Equal(AuthenticationFailure.Unavailable, Assert.IsType<AuthenticationRefused>(await Verify(fault, page.PageHandle)).Code);
        await AssertPassword(fixture.Password, 1, false);
        Assert.Equal(RecoveryEmailProvenance.Unknown, (await State()).RecoveryEmailProvenance);
        Assert.IsType<EmailVerificationCompleted>(await Verify(host, page.PageHandle));
    }

    [Fact]
    public async Task Lost_reset_commit_response_does_not_repeat_or_restore_consumed_authority()
    {
        using var host = new IdentityHttpHost(fixture); await Request(host, Email);
        var page = Assert.IsType<SecurityLinkOpened>(await Open(host, await Grant(host), AuthenticationOperationPurpose.PasswordResetEmail));
        using var fault = new IdentityHttpHost(fixture, null, null, new CompletionCommitFault());
        Assert.Equal(AuthenticationFailure.Unavailable, Assert.IsType<AuthenticationRefused>(await Reset(fault, page.PageHandle)).Code);
        await AssertPassword(NewPassword, 2, true);
        Assert.IsType<AuthenticationRefused>(await Reset(host, page.PageHandle));
        await AssertPassword(NewPassword, 2, true);
    }

    [Fact]
    public async Task Expiry_cleanup_removes_grant_payload_and_slot_without_changing_credentials()
    {
        using var host = new IdentityHttpHost(fixture); await Request(host, Email);
        clock.Advance(TimeSpan.FromMinutes(30));
        await using (var db = fixture.CreateContext())
        {
            Assert.Equal(1, await new SqlIdentitySecurityStore(db).CleanupAsync(clock.GetUtcNow()));
            var op = await db.Set<AuthenticationOperation>().SingleAsync();
            Assert.Empty(op.HandleDigest); Assert.Null(op.OutstandingLinkSlot); Assert.Null(op.Destination);
        }
        await AssertPassword(fixture.Password, 1, false);
        clock.Advance(TimeSpan.FromHours(25));
        await using (var db = fixture.CreateContext())
        {
            await new SqlIdentitySecurityStore(db).CleanupAsync(clock.GetUtcNow());
            Assert.Empty(await db.Set<AuthenticationOperation>().ToArrayAsync());
        }
    }

    private sealed class RequestSaveFault(bool afterSave) : SaveChangesInterceptor
    {
        private void Fail(DbContext? db, bool after)
        {
            if (after == afterSave && db?.ChangeTracker.Entries<AuthenticationOperation>().Any(x => x.Entity.State == AuthenticationOperationState.AwaitingExplicitSubmit) == true)
                throw new IOException("Synthetic grant persistence failure.");
        }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Fail(eventData.Context, false); return ValueTask.FromResult(result); }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { Fail(eventData.Context, true); return ValueTask.FromResult(result); }
    }
    private sealed class CompletionCommitFault : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<AuthenticationOperation>().Any(x => x.Entity.State == AuthenticationOperationState.Completed) == true)
                throw new IOException("Synthetic lost reset commit response.");
            return Task.CompletedTask;
        }
    }
}
