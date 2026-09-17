using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    [InlineData("empty-legacy")]
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
        if (scenario == "empty-legacy")
        {
            await db.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(s => s.ProtectedTotpSecret, (byte[]?)null));
            await db.Users.Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(u => u.TotpSecret, Array.Empty<byte>()));
        }
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
    public async Task Bounded_batches_advance_past_users_without_factors_and_resume_after_partial_progress()
    {
        var second = await fixture.CreateSyntheticUserAsync("migration-second-" + Guid.NewGuid().ToString("N"));
        var third = await fixture.CreateSyntheticUserAsync("migration-third-" + Guid.NewGuid().ToString("N"));
        await using var db = fixture.CreateContext();
        await db.Users.Where(x => x.ID == third).ExecuteUpdateAsync(x => x.SetProperty(u => u.TotpSecret, fixture.FactorSecret));
        var store = new SqlIdentitySecurityStore(db);
        var firstBatch = await store.MigrateLegacyTotpBatchAsync(second - 1, 1,
            (state, secret) => LegacyTotpMigration.CopyOrVerify(fixture.Protection, state, secret));
        Assert.Equal(second, firstBatch.LastUserID); Assert.Equal(1, firstBatch.Examined); Assert.Equal(0, firstBatch.Copied);
        var next = await store.MigrateLegacyTotpBatchAsync(firstBatch.LastUserID, 1,
            (state, secret) => LegacyTotpMigration.CopyOrVerify(fixture.Protection, state, secret));
        Assert.Equal(third, next.LastUserID); Assert.Equal(1, next.Examined); Assert.Equal(1, next.Copied);
        var done = await store.MigrateLegacyTotpBatchAsync(next.LastUserID, 1,
            (state, secret) => LegacyTotpMigration.CopyOrVerify(fixture.Protection, state, secret));
        Assert.Equal(0, done.Examined); Assert.Equal(third, done.LastUserID);
        Assert.Equal(0, (await Batch()).Copied);
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
