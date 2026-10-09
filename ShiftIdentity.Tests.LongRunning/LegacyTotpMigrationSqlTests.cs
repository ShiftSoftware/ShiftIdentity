using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OtpNet;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Sql")]
public sealed class LegacyTotpMigrationSqlTests(SqlIdentityFixture fixture) : IClassFixture<SqlIdentityFixture>, IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await fixture.ResetAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task SeedLegacy(bool inactive = false, bool deleted = false)
    {
        await using var db = fixture.CreateContext();
        await db.Users.IgnoreQueryFilters().Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x
            .SetProperty(u => u.TotpSecret, fixture.FactorSecret).SetProperty(u => u.IsActive, !inactive).SetProperty(u => u.IsDeleted, deleted));
    }

    private async Task<LegacyTotpMigrationBatch> Batch(params IInterceptor[] interceptors)
    {
        await using var db = fixture.CreateContext(interceptors);
        return await new SqlIdentitySecurityStore(db).MigrateLegacyTotpBatchAsync(0, 100,
            (state, secret) => LegacyTotpMigration.CopyOrVerify(fixture.Protection, state, secret));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Copy_preserves_plaintext_versions_and_factor_on_restart(bool inactive, bool deleted)
    {
        await SeedLegacy(inactive, deleted);
        Assert.Equal(1, (await Batch()).Copied);
        await using var db = fixture.CreateContext();
        var state = await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == fixture.UserID);
        Assert.Equal(fixture.FactorSecret, fixture.ReadSyntheticFactor(state));
        Assert.Equal(1, state.SecurityVersion); Assert.Equal(1, state.FactorGeneration);
        Assert.Null(state.LastAcceptedTotpStep);
        Assert.Equal(fixture.FactorSecret, (await db.Users.IgnoreQueryFilters().SingleAsync(x => x.ID == fixture.UserID)).TotpSecret);
        Assert.Equal(0, (await Batch()).Copied);
        Assert.Equal(state.ProtectedTotpSecret, (await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == fixture.UserID)).ProtectedTotpSecret);
        Assert.Empty(await db.Set<AuthenticationAuditEvent>().ToListAsync());
    }

    [Fact, Trait("Category", "Http")]
    public async Task Hosted_startup_copies_before_serving_and_existing_authenticator_logs_in()
    {
        await using var legacy = new SqlIdentityFixture { SeedLegacyFactorBeforeExpansion = true };
        await legacy.InitializeAsync();
        using var startup = new HostBuilder().ConfigureServices(s => IdentityHttpHost.AddAdmissionServices(s, legacy)).Build();
        await startup.StartAsync();
        using var http = new IdentityHttpHost(legacy);
        var pkce = IdentityHttpHost.Pkce();
        var challenge = Assert.IsType<ChallengeRequired>(await http.LoginAsync(legacy, pkce.Challenge)).Challenge;
        Assert.Equal(AuthenticationStep.ExistingMfa, challenge.Step);
        Assert.Null(challenge.NewAuthenticator);
        Assert.IsType<SessionIssued>(await http.CompleteAsync(challenge.Handle!,
            new Totp(legacy.FactorSecret).ComputeTotp(), pkce.Verifier));
        await startup.StopAsync();
    }

    [Fact]
    public async Task Two_instances_copy_once()
    {
        await SeedLegacy();
        using var gate = new AdmissionGate("copy");
        await using var firstDb = fixture.CreateContext();
        var first = Task.Run(() => new SqlIdentitySecurityStore(firstDb).MigrateLegacyTotpBatchAsync(0, 100,
            (state, secret) => { gate.Observe("copy"); LegacyTotpMigration.CopyOrVerify(fixture.Protection, state, secret); }));
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var signal = new SqlCommandSignal("UPDLOCK");
        var second = Batch(signal);
        try
        {
            await signal.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(second.IsCompleted);
        }
        finally { gate.Release(); }
        var results = await Task.WhenAll(first, second);
        Assert.Equal(1, results.Sum(x => x.Copied));
        await using var db = fixture.CreateContext();
        Assert.Equal(fixture.FactorSecret, fixture.ReadSyntheticFactor(await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID)));
    }

    [Fact]
    public async Task Recovery_and_a_newer_factor_are_not_overwritten_from_the_old_column()
    {
        await SeedLegacy();
        await using var db = fixture.CreateContext();
        var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
        state.LocalMfaRecoveryRequired = true;
        await db.SaveChangesAsync();
        Assert.Equal(0, (await Batch()).Copied);
        Assert.Null((await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == fixture.UserID)).ProtectedTotpSecret);
        state.LocalMfaRecoveryRequired = false;
        var newer = RandomNumberGenerator.GetBytes(20);
        fixture.SetSyntheticFactor(state, newer);
        await db.SaveChangesAsync();
        Assert.Equal(0, (await Batch()).Copied);
        Assert.Equal(newer, fixture.ReadSyntheticFactor(await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == fixture.UserID)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_save_rolls_back_and_restart_recovers(bool afterSave)
    {
        await SeedLegacy();
        await Assert.ThrowsAsync<IdentitySecurityUnavailableException>(() => Batch(new FactorSaveFault(afterSave)));
        await using var db = fixture.CreateContext();
        Assert.Null((await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID)).ProtectedTotpSecret);
        Assert.Equal(1, (await Batch()).Copied);
    }

    [Fact]
    public async Task Lost_commit_response_resumes_without_reencrypting()
    {
        await SeedLegacy();
        await Assert.ThrowsAsync<IdentitySecurityUnavailableException>(() => Batch(new LostCommitResponse()));
        await using var db = fixture.CreateContext();
        var saved = (await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == fixture.UserID)).ProtectedTotpSecret;
        Assert.NotNull(saved);
        Assert.Equal(0, (await Batch()).Copied);
        Assert.Equal(saved, (await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == fixture.UserID)).ProtectedTotpSecret);
    }

    [Theory]
    [InlineData("missing-key")]
    [InlineData("wrong-key")]
    [InlineData("retired-key")]
    [InlineData("missing-state")]
    [InlineData("corrupt-factor")]
    public async Task Incomplete_migration_stops_host_startup_without_disclosing_secrets(string scenario)
    {
        await SeedLegacy();
        await Batch();
        var configuration = new ShiftIdentityConfiguration { FactorProtection = fixture.FactorProtection };
        await using var db = fixture.CreateContext();
        if (scenario == "missing-key") configuration.FactorProtection = new();
        if (scenario == "wrong-key") configuration.FactorProtection = IdentityMaterialProtectionTests.Settings("fixture-key");
        if (scenario == "retired-key") configuration.FactorProtection = IdentityMaterialProtectionTests.Settings("replacement-key");
        if (scenario == "missing-state") await db.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID).ExecuteDeleteAsync();
        if (scenario == "corrupt-factor") await db.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.ProtectedTotpSecret, new byte[] { 1, 2, 3 }));
        try
        {
            using var host = new HostBuilder().ConfigureServices(s =>
            {
                s.AddSingleton(configuration);
                IdentityHttpHost.AddAdmissionServices(s, fixture);
            }).Build();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
            Assert.Null(error.InnerException);
            Assert.DoesNotContain(Convert.ToBase64String(fixture.FactorSecret), error.ToString());
            Assert.DoesNotContain(fixture.FactorProtection.Keys["fixture-key"], error.ToString());
        }
        finally
        {
            if (scenario == "missing-state") { db.Add(new UserSecurityState { UserID = fixture.UserID }); await db.SaveChangesAsync(); }
        }
    }

    [Fact]
    public async Task Bounded_batches_visit_only_users_whose_factor_needs_a_copy_and_resume_after_partial_progress()
    {
        var second = await fixture.CreateSyntheticUserAsync("migration-second-" + Guid.NewGuid().ToString("N"));
        var third = await fixture.CreateSyntheticUserAsync("migration-third-" + Guid.NewGuid().ToString("N"));
        var fourth = await fixture.CreateSyntheticUserAsync("migration-fourth-" + Guid.NewGuid().ToString("N"));
        await using var db = fixture.CreateContext();
        await db.Users.Where(x => x.ID == third || x.ID == fourth).ExecuteUpdateAsync(x => x.SetProperty(u => u.TotpSecret, fixture.FactorSecret));
        var store = new SqlIdentitySecurityStore(db);
        // The user without a factor is not visited: a batch of one goes straight to the next user with one.
        var firstBatch = await store.MigrateLegacyTotpBatchAsync(second - 1, 1,
            (state, secret) => LegacyTotpMigration.CopyOrVerify(fixture.Protection, state, secret));
        Assert.Equal(third, firstBatch.LastUserID); Assert.Equal(1, firstBatch.Examined); Assert.Equal(1, firstBatch.Copied);
        var next = await store.MigrateLegacyTotpBatchAsync(firstBatch.LastUserID, 1,
            (state, secret) => LegacyTotpMigration.CopyOrVerify(fixture.Protection, state, secret));
        Assert.Equal(fourth, next.LastUserID); Assert.Equal(1, next.Examined); Assert.Equal(1, next.Copied);
        var done = await store.MigrateLegacyTotpBatchAsync(next.LastUserID, 1,
            (state, secret) => LegacyTotpMigration.CopyOrVerify(fixture.Protection, state, secret));
        Assert.Equal(0, done.Examined); Assert.Equal(fourth, done.LastUserID);
        // Copied factors are not visited again.
        Assert.Equal(0, (await Batch()).Examined);
    }

    [Fact]
    public async Task A_start_with_nothing_left_to_copy_locks_no_user()
    {
        await SeedLegacy();
        await Batch();
        var locks = new SqlCommandSignal("UPDLOCK");
        using var host = new HostBuilder().ConfigureServices(s => IdentityHttpHost.AddAdmissionServices(s, fixture, interceptors: [locks])).Build();
        await host.StartAsync();
        Assert.False(locks.Entered.IsCompleted);
        await host.StopAsync();
    }

    private async Task SetLegacy(long userID, byte[]? secret)
    {
        await using var db = fixture.CreateContext();
        await db.Users.IgnoreQueryFilters().Where(x => x.ID == userID).ExecuteUpdateAsync(x => x.SetProperty(u => u.TotpSecret, secret));
    }

    private static Task<byte[]?> Legacy(ShiftSoftware.ShiftIdentity.Data.ShiftIdentityDbContext db, long userID) =>
        db.Users.IgnoreQueryFilters().AsNoTracking().Where(x => x.ID == userID).Select(x => x.TotpSecret).SingleAsync();

    /// <summary>
    /// An empty legacy factor (0x: not NULL, but no bytes) is not a factor. Releases 2026.9.21.1 to 2026.10.7.2 stored one
    /// for every user created through the dashboard, and the next start of the authority stopped. Now the start clears it
    /// to NULL in that user's locked transaction, whatever the security row holds, and logs only how many it cleared. The
    /// next start does not visit those users.
    /// </summary>
    [Fact]
    public async Task An_empty_legacy_factor_is_cleared_and_the_next_start_does_not_visit_it()
    {
        var enrolled = await fixture.CreateSyntheticUserAsync("migration-empty-enrolled-" + Guid.NewGuid().ToString("N"), mfa: true);
        await SetLegacy(fixture.UserID, []);
        await SetLegacy(enrolled, []);
        var logs = new LogCapture();
        using (var host = new HostBuilder().ConfigureServices(s =>
        {
            s.AddLogging(x => x.AddProvider(logs));
            IdentityHttpHost.AddAdmissionServices(s, fixture);
        }).Build())
        {
            await host.StartAsync();
            await host.StopAsync();
        }
        await using var db = fixture.CreateContext();
        Assert.Null(await Legacy(db, fixture.UserID));
        Assert.Null(await Legacy(db, enrolled));
        // Nothing was copied for the user without a factor, and the enrolled factor is unchanged.
        Assert.Null((await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == fixture.UserID)).ProtectedTotpSecret);
        Assert.Equal(fixture.FactorSecret, fixture.ReadSyntheticFactor(await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == enrolled)));
        // Only the number is logged.
        Assert.Contains("Identity factor migration cleared 2 empty legacy factors (an empty value is not a factor).", logs.Messages);
        Assert.DoesNotContain(logs.Messages, x => x.Contains(Convert.ToBase64String(fixture.FactorSecret)));
        // The next start visits neither user, so it locks no user.
        var locks = new SqlCommandSignal("UPDLOCK");
        using var next = new HostBuilder().ConfigureServices(s => IdentityHttpHost.AddAdmissionServices(s, fixture, interceptors: [locks])).Build();
        await next.StartAsync();
        Assert.False(locks.Entered.IsCompleted);
        await next.StopAsync();
    }

    /// <summary>A real legacy factor is still copied and verified, at the same start that clears an empty one.</summary>
    [Fact]
    public async Task A_real_legacy_factor_is_still_copied_and_verified_beside_an_empty_one()
    {
        var real = await fixture.CreateSyntheticUserAsync("migration-real-" + Guid.NewGuid().ToString("N"));
        await SetLegacy(fixture.UserID, []);
        await SetLegacy(real, fixture.FactorSecret);
        using (var host = new HostBuilder().ConfigureServices(s => IdentityHttpHost.AddAdmissionServices(s, fixture)).Build())
        {
            await host.StartAsync();
            await host.StopAsync();
        }
        await using var db = fixture.CreateContext();
        Assert.Null(await Legacy(db, fixture.UserID));
        Assert.Equal(fixture.FactorSecret, fixture.ReadSyntheticFactor(await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == real)));
        // As before, the copy keeps the legacy column of a real factor.
        Assert.Equal(fixture.FactorSecret, await Legacy(db, real));
    }

    /// <summary>
    /// A non-empty legacy factor whose copy fails still stops the start, as before. The copy has no check of its own on
    /// the plaintext: any non-empty value is protected as it is. It fails when the protection, its verification or the save
    /// fails, and a save fault stands in for that here. The empty factor of an earlier user is still cleared, because each
    /// user commits on its own.
    /// </summary>
    [Fact]
    public async Task A_legacy_factor_that_fails_to_copy_still_stops_the_start()
    {
        var broken = await fixture.CreateSyntheticUserAsync("migration-broken-" + Guid.NewGuid().ToString("N"));
        await SetLegacy(fixture.UserID, []);
        await SetLegacy(broken, fixture.FactorSecret);
        try
        {
            using var host = new HostBuilder().ConfigureServices(s =>
                IdentityHttpHost.AddAdmissionServices(s, fixture, interceptors: [new CopyFault(broken)])).Build();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
            Assert.Null(error.InnerException);
            Assert.DoesNotContain(Convert.ToBase64String(fixture.FactorSecret), error.ToString());
            await using var db = fixture.CreateContext();
            Assert.Null(await Legacy(db, fixture.UserID));
            Assert.Equal(fixture.FactorSecret, await Legacy(db, broken));
            Assert.Null((await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == broken)).ProtectedTotpSecret);
        }
        // Later tests in this class start hosts on the same database.
        finally { await SetLegacy(broken, null); }
    }

    [Fact]
    public async Task Protected_factor_pages_advance_in_user_order_and_skip_users_without_one()
    {
        var first = await fixture.CreateSyntheticUserAsync("migration-page-a-" + Guid.NewGuid().ToString("N"), mfa: true);
        _ = await fixture.CreateSyntheticUserAsync("migration-page-b-" + Guid.NewGuid().ToString("N"));
        var last = await fixture.CreateSyntheticUserAsync("migration-page-c-" + Guid.NewGuid().ToString("N"), mfa: true);
        await using var db = fixture.CreateContext();
        var store = new SqlIdentitySecurityStore(db);
        Assert.Equal(first, Assert.Single(await store.ReadProtectedFactorsAsync(first - 1, 1)).UserID);
        var page = Assert.Single(await store.ReadProtectedFactorsAsync(first, 1));
        Assert.Equal(last, page.UserID);
        Assert.Equal(fixture.FactorSecret, fixture.ReadSyntheticFactor(page));
        Assert.Empty(await store.ReadProtectedFactorsAsync(last, 1000));
    }

    /// <summary>Fails the save that writes a protected factor for one user.</summary>
    private sealed class CopyFault(long userID) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<UserSecurityState>()
                    .Any(x => x.Entity.UserID == userID && x.Entity.ProtectedTotpSecret is not null))
                throw new IOException("Synthetic factor save failure.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class LogCapture : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Enqueue(formatter(state, exception));
        public void Dispose() { }
    }

    private sealed class FactorSaveFault(bool afterSave) : SaveChangesInterceptor
    {
        private static void Fail(DbContext? db)
        {
            if (db!.ChangeTracker.Entries<UserSecurityState>().Any(x => x.Entity.ProtectedTotpSecret is not null))
                throw new IOException("Synthetic factor save failure.");
        }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { if (!afterSave) Fail(eventData.Context); return ValueTask.FromResult(result); }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { if (afterSave) Fail(eventData.Context); return ValueTask.FromResult(result); }
    }
}
