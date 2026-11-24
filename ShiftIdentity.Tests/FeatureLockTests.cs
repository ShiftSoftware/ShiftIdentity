using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Team;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// The feature lock: a repository's save of a row whose feature is locked is refused, and nothing is written. It is a
/// before-save handler that the identity context declares, so no host registers it, and it is a guard, so a save that
/// switches the handlers off does not get past it. Only code that names it does. It takes nothing in its constructor
/// and asks the save's services for what it needs, so it is also tested on its own, with rows that
/// <see cref="HandlerRows"/> builds. SQLite stores the identity model.
/// </summary>
public sealed class FeatureLockTests : IAsyncDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private ServiceProvider? provider;

    // A host as the dashboard's registration leaves it, with or without feature locking configured.
    private async Task<AsyncServiceScope> HostAsync(ShiftIdentityFeatureLocking? locking)
    {
        var services = new ServiceCollection();
        services.AddDbContext<SqliteIdentityTestDbContext>(options => options.UseSqlite(connection));
        services.AddLogging();
        services.AddLocalization();
        services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));

        // What a repository needs to be built.
        services.AddSingleton(Substitute.For<IDefaultDataLevelAccess>());
        services.AddSingleton<IHashIdService>(new HashIdService(Options.Create(new ShiftEntityOptions())));
        services.AddSingleton(Substitute.For<ICurrentUserProvider>());
        services.AddScoped<IdentityClaimProvider>();

        if (locking is not null)
            services.AddSingleton(locking);

        provider = services.BuildServiceProvider(validateScopes: true);

        await connection.OpenAsync(Ct);
        var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SqliteIdentityTestDbContext>().Database.EnsureCreatedAsync(Ct);

        return scope;
    }

    public async ValueTask DisposeAsync()
    {
        if (provider is not null)
            await provider.DisposeAsync();

        await connection.DisposeAsync();
    }

    private static ShiftRepository<SqliteIdentityTestDbContext, Team, TeamListDTO, TeamDTO> Teams(SqliteIdentityTestDbContext db) => new(db);

    // The refusal, in the English text that the invariant culture gets.
    private static async Task<ShiftEntityException> RefusalAsync(Func<Task> save)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        try
        {
            return await Assert.ThrowsAsync<ShiftEntityException>(save);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public async Task A_repository_save_of_a_row_of_a_locked_feature_is_refused_and_nothing_is_written()
    {
        await using var scope = await HostAsync(new ShiftIdentityFeatureLocking { TeamFeatureIsLocked = true });
        var db = scope.ServiceProvider.GetRequiredService<SqliteIdentityTestDbContext>();

        // Added to the context and saved through the repository, as an endpoint's save is.
        var teams = Teams(db);
        db.Teams.Add(new Team { Name = "Ops" });

        var refusal = await RefusalAsync(() => teams.SaveChangesAsync());

        Assert.Equal("Error", refusal.Message.Title);
        Assert.Contains("Team", refusal.Message.Body);
        Assert.Contains("locked", refusal.Message.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.Teams.AsNoTracking().CountAsync(Ct));

        // A feature that is not locked saves through its repository.
        db.ChangeTracker.Clear();

        var trees = new ShiftRepository<SqliteIdentityTestDbContext, AccessTree, AccessTreeListDTO, AccessTreeDTO>(db);
        db.AccessTrees.Add(new AccessTree { Name = "Shared", Tree = "{}" });
        await trees.SaveChangesAsync();

        Assert.Equal(1, await db.AccessTrees.AsNoTracking().CountAsync(Ct));
    }

    [Fact]
    public async Task A_save_through_the_context_itself_is_not_locked()
    {
        // Seeding, replication and migrations save through the context, and were never locked.
        await using var scope = await HostAsync(new ShiftIdentityFeatureLocking { TeamFeatureIsLocked = true });
        var db = scope.ServiceProvider.GetRequiredService<SqliteIdentityTestDbContext>();

        db.Teams.Add(new Team { Name = "Seeded" });
        await db.SaveChangesAsync(Ct);

        Assert.Equal(1, await db.Teams.AsNoTracking().CountAsync(Ct));
    }

    [Fact]
    public async Task The_lock_is_a_guard_that_only_code_which_names_it_switches_off()
    {
        await using var scope = await HostAsync(new ShiftIdentityFeatureLocking { TeamFeatureIsLocked = true });
        var db = scope.ServiceProvider.GetRequiredService<SqliteIdentityTestDbContext>();

        // Added to the context and saved through the repository, as an endpoint's save is.
        var teams = Teams(db);
        db.Teams.Add(new Team { Name = "Ops" });

        // A save that switches every handler off, or the whole before-save stage, is still refused.
        using (db.SuppressAllSaveHandlers())
            await RefusalAsync(() => teams.SaveChangesAsync());

        using (db.SuppressSaveHandlers(SaveStages.BeforeSave | SaveStages.AfterSave | SaveStages.BeforeCommit | SaveStages.AfterCommit))
            await RefusalAsync(() => teams.SaveChangesAsync());

        using (db.SuppressAfterCommit())
            await RefusalAsync(() => teams.SaveChangesAsync());

        Assert.Equal(0, await db.Teams.AsNoTracking().CountAsync(Ct));

        // Code that names the lock says that it means to save past it.
        using (db.SuppressSaveHandler<FeatureLock>())
            await teams.SaveChangesAsync();

        Assert.Equal(1, await db.Teams.AsNoTracking().CountAsync(Ct));
    }

    [Fact]
    public async Task A_host_that_configured_no_feature_locking_has_nothing_to_enforce()
    {
        await using var scope = await HostAsync(locking: null);
        var db = scope.ServiceProvider.GetRequiredService<SqliteIdentityTestDbContext>();

        // Added to the context and saved through the repository, as an endpoint's save is.
        var teams = Teams(db);
        db.Teams.Add(new Team { Name = "Ops" });
        await teams.SaveChangesAsync();

        Assert.Equal(1, await db.Teams.AsNoTracking().CountAsync(Ct));
    }

    // ── The lock on its own, called with rows that a test builds ────────────────

    // The lock, called as the before-save stage calls it for one new team.
    private static Task CallAsync(SqliteIdentityTestDbContext db, bool isRepositorySave) =>
        new FeatureLock().BeforeSaveAsync(
            HandlerRows.Pending(db, isRepositorySave, new ChangedRow<Team>(new Team { Name = "Ops" }, RowChange.Added)), Ct).AsTask();

    [Fact]
    public async Task Called_with_the_rows_of_a_repository_save_the_lock_refuses_a_locked_feature_and_nothing_else()
    {
        await using var scope = await HostAsync(new ShiftIdentityFeatureLocking { TeamFeatureIsLocked = true });
        var db = scope.ServiceProvider.GetRequiredService<SqliteIdentityTestDbContext>();

        // A repository's save of a row of the locked feature.
        var refusal = await RefusalAsync(() => CallAsync(db, isRepositorySave: true));

        Assert.Equal("Error", refusal.Message.Title);
        Assert.Contains("Team", refusal.Message.Body);

        // Any other save of it is not locked, and neither is a repository's save of a feature that is not locked.
        await CallAsync(db, isRepositorySave: false);

        var tree = new ChangedRow<AccessTree>(new AccessTree { Name = "Shared", Tree = "{}" }, RowChange.Added);
        await new FeatureLock().BeforeSaveAsync(HandlerRows.Pending(db, isRepositorySave: true, tree), Ct);

        // A set with no rows is nothing to refuse.
        await new FeatureLock().BeforeSaveAsync(HandlerRows.Pending<Team>(db, isRepositorySave: true), Ct);
    }

    [Fact]
    public async Task The_lock_asks_the_services_of_the_save_which_a_context_built_by_hand_has_only_once_they_are_attached()
    {
        await using var scope = await HostAsync(new ShiftIdentityFeatureLocking { TeamFeatureIsLocked = true });

        // A context that a tool builds has no services: no feature locking to enforce, and nothing to look it up in.
        await using var db = new SqliteIdentityTestDbContext(new DbContextOptionsBuilder<SqliteIdentityTestDbContext>().UseSqlite(connection).Options);

        await CallAsync(db, isRepositorySave: true);

        // Given the services of a host that locks the feature, the same context has the lock.
        db.AttachServices(scope.ServiceProvider);

        var refusal = await RefusalAsync(() => CallAsync(db, isRepositorySave: true));

        Assert.Contains("locked", refusal.Message.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_lock_is_the_public_guard_FeatureLock_which_takes_nothing_and_has_no_other_name()
    {
        var type = typeof(FeatureLock);

        // Code names it to switch it off, so the name is public, and the blanket switches pass it by.
        Assert.True(type.IsPublic);
        Assert.Single(type.GetCustomAttributes(typeof(GuardAttribute), inherit: false));

        // Its only constructor takes nothing, so every identity context can create it, also one built by hand.
        Assert.Empty(Assert.Single(type.GetConstructors()).GetParameters());

        // It was FeatureLockSaveValidator. The rename left no alias behind.
        Assert.DoesNotContain(type.Assembly.GetTypes(), other => other.Name == "FeatureLockSaveValidator");
    }

    [Fact]
    public async Task An_identity_context_built_without_services_saves_the_rows_the_lock_is_declared_for()
    {
        // A tool, a test fixture and a seeder build the context with its options alone. The lock's constructor takes
        // nothing, so the context can create it there, and a save through the context is not a repository's.
        await connection.OpenAsync(Ct);

        await using var db = new SqliteIdentityTestDbContext(new DbContextOptionsBuilder<SqliteIdentityTestDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(Ct);

        db.Teams.Add(new Team { Name = "Seeded" });
        db.AccessTrees.Add(new AccessTree { Name = "Seeded", Tree = "{}" });
        await db.SaveChangesAsync(Ct);

        Assert.Equal(1, await db.Teams.AsNoTracking().CountAsync(Ct));
    }
}
