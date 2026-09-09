using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Sql")]
public sealed class SecurityEmailLookupSqlTests(SqlIdentityFixture fixture) : IClassFixture<SqlIdentityFixture>
{
    private static readonly AuthenticationClient Client = new("test-client", "test-api");

    [Fact]
    public async Task Lookup_requires_explicit_keys_and_a_weak_contact_write_cannot_be_adopted()
    {
        await fixture.ResetAsync();
        await InitializeAsync(fixture.UserID);
        await using var db = fixture.CreateContext();
        var store = new SqlIdentitySecurityStore(db);
        var lookup = Assert.IsType<SecurityEmailLookup>(await store.ResolveSecurityEmailAsync(" " + fixture.Username.ToUpperInvariant() + " ", TestContext.Current.CancellationToken));
        Assert.True(await store.AdmitAsync(lookup.UserID, null, Client, _ => store.RecheckSecurityEmailAsync(lookup, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));
        await using (var weakWriter = fixture.CreateContext())
        {
            (await weakWriter.Users.SingleAsync(x => x.ID == fixture.UserID, TestContext.Current.CancellationToken)).Email = "weak-profile@example.invalid";
            await weakWriter.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        Assert.Null(await store.ResolveSecurityEmailAsync("weak-profile@example.invalid", TestContext.Current.CancellationToken));
        Assert.False(await store.AdmitAsync(lookup.UserID, null, Client, _ => store.RecheckSecurityEmailAsync(lookup, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_previously_unique_lookup_is_refused_after_a_cross_field_match_appears()
    {
        await fixture.ResetAsync();
        var target = await fixture.CreateSyntheticUserAsync("lookup-unique@example.invalid");
        var other = await fixture.CreateSyntheticUserAsync("lookup-other");
        await InitializeAsync(target);
        await InitializeAsync(other);
        await using var db = fixture.CreateContext();
        var store = new SqlIdentitySecurityStore(db);
        var lookup = Assert.IsType<SecurityEmailLookup>(await store.ResolveSecurityEmailAsync("lookup-unique@example.invalid", TestContext.Current.CancellationToken));
        await ChangeEmailAsync(other, "lookup-unique@example.invalid");

        Assert.False(await store.AdmitAsync(target, null, Client, _ => store.RecheckSecurityEmailAsync(lookup, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));
        Assert.Null(await store.ResolveSecurityEmailAsync("lookup-unique@example.invalid", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_email_lookup_is_refused_when_the_contact_changes_before_admission()
    {
        await fixture.ResetAsync();
        await InitializeAsync(fixture.UserID);
        await ChangeEmailAsync(fixture.UserID, "old-lookup@example.invalid");
        await using var db = fixture.CreateContext();
        var store = new SqlIdentitySecurityStore(db);
        var lookup = Assert.IsType<SecurityEmailLookup>(await store.ResolveSecurityEmailAsync("old-lookup@example.invalid", TestContext.Current.CancellationToken));
        await ChangeEmailAsync(fixture.UserID, "new-lookup@example.invalid");

        Assert.False(await store.AdmitAsync(lookup.UserID, null, Client, _ => store.RecheckSecurityEmailAsync(lookup, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Normalized_email_collision_rolls_back_the_contact_and_version_changes()
    {
        await fixture.ResetAsync();
        await InitializeAsync(fixture.UserID);
        var other = await fixture.CreateSyntheticUserAsync("lookup-collision-other");
        await InitializeAsync(other);
        await ChangeEmailAsync(fixture.UserID, "existing@example.invalid");
        await using var before = fixture.CreateContext();
        var initial = await before.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == other, TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<Exception>(() => ChangeEmailAsync(other, " EXISTING@EXAMPLE.INVALID "));

        await using var after = fixture.CreateContext();
        var user = await after.Users.SingleAsync(x => x.ID == other, TestContext.Current.CancellationToken);
        var state = await after.Set<UserSecurityState>().SingleAsync(x => x.UserID == other, TestContext.Current.CancellationToken);
        Assert.Null(user.Email);
        Assert.Null(state.EmailLookupKey);
        Assert.Equal(initial.SecurityVersion, state.SecurityVersion);
        Assert.Equal(initial.ContactRevision, state.ContactRevision);
    }

    [Theory]
    [InlineData("username")]
    [InlineData("email")]
    public async Task Opted_in_model_enforces_unique_authority_keys_independently_of_legacy_user_indexes(string field)
    {
        await fixture.ResetAsync();
        await InitializeAsync(fixture.UserID);
        var other = await fixture.CreateSyntheticUserAsync("lookup-index-other-" + field);
        await InitializeAsync(other);
        await ChangeEmailAsync(fixture.UserID, "lookup-index@example.invalid");
        await using var db = fixture.CreateContext();
        var original = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID, TestContext.Current.CancellationToken);
        var collision = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == other, TestContext.Current.CancellationToken);
        // User rows keep different values: the failure must come from the authority index itself.
        if (field == "username") collision.UsernameLookupKey = original.UsernameLookupKey;
        else collision.EmailLookupKey = original.EmailLookupKey;

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        var sql = Assert.IsType<SqlException>(error.InnerException);
        Assert.Contains(sql.Number, new[] { 2601, 2627 });
        Assert.Contains(field == "username" ? "IX_UserSecurityStates_UsernameLookupKey" : "IX_UserSecurityStates_EmailLookupKey", sql.Message);
    }

    [Fact]
    public async Task Lookup_range_locks_prevent_a_cross_field_match_until_admission_commits()
    {
        await fixture.ResetAsync();
        var target = await fixture.CreateSyntheticUserAsync("lookup-range@example.invalid");
        var other = await fixture.CreateSyntheticUserAsync("lookup-range-other");
        await InitializeAsync(target);
        await InitializeAsync(other);
        await using var db = fixture.CreateContext();
        var store = new SqlIdentitySecurityStore(db);
        var lookup = Assert.IsType<SecurityEmailLookup>(await store.ResolveSecurityEmailAsync("lookup-range@example.invalid", TestContext.Current.CancellationToken));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var admission = store.AdmitAsync(target, null, Client, async _ =>
        {
            Assert.True(await store.RecheckSecurityEmailAsync(lookup, TestContext.Current.CancellationToken));
            entered.TrySetResult();
            await release.Task.WaitAsync(TestContext.Current.CancellationToken);
            return true;
        }, TestContext.Current.CancellationToken);
        var command = new SqlCommandSignal("UPDATE");
        await using var writer = fixture.CreateContext(command);
        Task<bool>? mutation = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            mutation = new SqlIdentitySecurityStore(writer).AdmitAsync(other, null, Client, unit =>
                Task.FromResult(RecoveryContact.ApplyAuthorizedEmailChange(unit, unit.Security.SecurityVersion, unit.Security.ContactRevision,
                    "lookup-range@example.invalid", RecoveryEmailProvenance.TrustedAdminAssignment, fixture.Clock.GetUtcNow())), TestContext.Current.CancellationToken);
            await command.Entered.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(mutation.IsCompleted);
        }
        finally { release.TrySetResult(); }
        Assert.True(await admission);
        Assert.True(await mutation!);
    }

    [Fact]
    public async Task Cleanup_expires_pending_links_and_bounds_operation_and_throttle_retention_without_deleting_current_rows()
    {
        await fixture.ResetAsync();
        var now = fixture.Clock.GetUtcNow();
        var expired = NewLink(AuthenticationOperationState.AwaitingExplicitSubmit, now.AddHours(-1), now.AddMinutes(-1));
        expired.OutstandingLinkSlot = $"{fixture.UserID}:verify";
        expired.Destination = "expired@example.invalid";
        expired.HandleDigest = [1];
        var current = NewLink(AuthenticationOperationState.AwaitingExplicitSubmit, now, now.AddMinutes(30));
        current.OutstandingLinkSlot = $"{fixture.UserID}:reset";
        current.Purpose = AuthenticationOperationPurpose.PasswordResetManual;
        current.Destination = "current@example.invalid";
        current.HandleDigest = [2];
        var recent = NewLink(AuthenticationOperationState.Completed, now.AddHours(-26), now.AddHours(-25), now);
        await using (var seed = fixture.CreateContext())
        {
            for (var i = 0; i < 105; i++)
            {
                seed.Set<AuthenticationOperation>().Add(NewLink(AuthenticationOperationState.Completed,
                    now.AddHours(-27), now.AddHours(-26), now.AddHours(-25)));
                seed.Set<AuthThrottleBucket>().Add(new() { Key = "old-bucket-" + i, WindowStart = now.AddHours(-25), Count = 20 });
            }
            seed.Set<AuthenticationOperation>().AddRange(expired, current, recent);
            seed.Set<AuthThrottleBucket>().Add(new() { Key = "current-bucket", WindowStart = now, Count = 20 });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using var db = fixture.CreateContext();
        var store = new SqlIdentitySecurityStore(db);

        Assert.Equal(1, await store.CleanupAsync(now, TestContext.Current.CancellationToken));
        Assert.Equal(8, await db.Set<AuthenticationOperation>().CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(5, await db.Set<AuthenticationOperation>().CountAsync(x => x.CompletedAt < now.AddHours(-24), TestContext.Current.CancellationToken));
        Assert.Equal(6, await db.Set<AuthThrottleBucket>().CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await store.CleanupAsync(now, TestContext.Current.CancellationToken));

        var retained = await db.Set<AuthenticationOperation>().AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, retained.Count);
        Assert.Contains(retained, x => x.ID == recent.ID && x.State == AuthenticationOperationState.Completed && x.CompletedAt == now);
        var cancelled = Assert.Single(retained, x => x.ID == expired.ID);
        Assert.Equal(AuthenticationOperationState.Cancelled, cancelled.State);
        Assert.Equal(now, cancelled.CompletedAt);
        Assert.Empty(cancelled.HandleDigest);
        Assert.Null(cancelled.OutstandingLinkSlot);
        Assert.Null(cancelled.Destination);
        var pending = Assert.Single(retained, x => x.ID == current.ID);
        Assert.Equal(AuthenticationOperationState.AwaitingExplicitSubmit, pending.State);
        Assert.Equal(current.HandleDigest, pending.HandleDigest);
        Assert.Equal(current.OutstandingLinkSlot, pending.OutstandingLinkSlot);
        Assert.Equal(current.Destination, pending.Destination);
        Assert.Null(pending.CompletedAt);
        var audit = await db.Set<AuthenticationAuditEvent>().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("OperationExpired", audit.Outcome);
        Assert.Equal(expired.ID, audit.OperationID);
        Assert.Equal(1, (await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == fixture.UserID, TestContext.Current.CancellationToken)).SecurityVersion);
        Assert.Equal("current-bucket", (await db.Set<AuthThrottleBucket>().SingleAsync(TestContext.Current.CancellationToken)).Key);

        AuthenticationOperation NewLink(AuthenticationOperationState state, DateTimeOffset createdAt, DateTimeOffset expiresAt,
            DateTimeOffset? completedAt = null) => new()
        {
            ID = Guid.NewGuid(), UserID = fixture.UserID, Purpose = AuthenticationOperationPurpose.EmailVerify,
            State = state, SecurityVersion = 1, ContactRevision = 1, FactorGeneration = 1, PolicyRevision = 1,
            ClientID = Client.ID, Audience = Client.Audience, CreatedAt = createdAt, ExpiresAt = expiresAt, CompletedAt = completedAt
        };
    }

    private async Task InitializeAsync(long id)
    {
        await using var db = fixture.CreateContext();
        var user = await db.Users.SingleAsync(x => x.ID == id, TestContext.Current.CancellationToken);
        var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == id, TestContext.Current.CancellationToken);
        RecoveryContact.InitializeLookup(user, state);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<bool> ChangeEmailAsync(long id, string email)
    {
        await using var db = fixture.CreateContext();
        return await new SqlIdentitySecurityStore(db).AdmitAsync(id, null, Client, unit => Task.FromResult(
            RecoveryContact.ApplyAuthorizedEmailChange(unit, unit.Security.SecurityVersion, unit.Security.ContactRevision, email,
                RecoveryEmailProvenance.TrustedAdminAssignment, fixture.Clock.GetUtcNow())), TestContext.Current.CancellationToken);
    }
}
