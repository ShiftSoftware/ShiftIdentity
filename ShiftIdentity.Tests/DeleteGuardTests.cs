using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Team;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.TypeAuth.Core;
using System.Globalization;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// Identity refuses to delete a named access tree that a user who is not deleted still has, and a team that still has
/// a member who is not deleted. A deleted tree or team still applies at sign-in, so deleting one that is in use would
/// leave grants and team claims that no screen shows. The deletes go through the built-in repository's
/// <c>DeleteAsync</c>, as the dashboard's endpoints do. SQLite stores the identity model.
/// </summary>
public sealed class DeleteGuardTests : IAsyncLifetime
{
    public enum Row { AccessTree, Team }

    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ServiceProvider provider;
    private AsyncServiceScope scope;
    private SqliteIdentityTestDbContext db = null!;

    public DeleteGuardTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<SqliteIdentityTestDbContext>(options => options.UseSqlite(connection));
        services.AddScoped<ShiftIdentityDbContext>(sp => sp.GetRequiredService<SqliteIdentityTestDbContext>());
        services.AddLogging();
        services.AddLocalization();
        services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));

        // What a repository needs to be built. Data-level access allows every delete here: the guard is what is tested.
        var dataLevelAccess = Substitute.For<IDefaultDataLevelAccess>();
        dataLevelAccess.HasDefaultDataLevelAccess(Arg.Any<DefaultDataLevelAccessOptions>(), Arg.Any<AccessTree?>(), Arg.Any<Access>()).Returns(true);
        dataLevelAccess.HasDefaultDataLevelAccess(Arg.Any<DefaultDataLevelAccessOptions>(), Arg.Any<Team?>(), Arg.Any<Access>()).Returns(true);
        services.AddSingleton(dataLevelAccess);
        services.AddSingleton<IHashIdService>(new HashIdService(Options.Create(new ShiftEntityOptions())));
        services.AddSingleton(Substitute.For<ICurrentUserProvider>());
        services.AddScoped<IdentityClaimProvider>();

        provider = services.BuildServiceProvider(validateScopes: true);
    }

    public async ValueTask InitializeAsync()
    {
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        scope = provider.CreateAsyncScope();
        db = scope.ServiceProvider.GetRequiredService<SqliteIdentityTestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await provider.DisposeAsync();
        await connection.DisposeAsync();
    }

    [Theory]
    [InlineData(Row.AccessTree)]
    [InlineData(Row.Team)]
    public async Task A_row_in_use_cannot_be_deleted_and_is_left_as_it_was(Row row)
    {
        // An inactive user can be made active again and would get the grants back, so only deleted users do not count.
        var id = await UsedByAsync(row, await UserAsync("carol", active: false), await UserAsync("bob", deleted: true), await UserAsync("alice"));

        var refusal = await RefusalAsync(row, id);

        Assert.Equal("Error", refusal.Message.Title);
        Assert.Equal(Refusal(row, "alice, carol", 2), refusal.Message.Body);

        // The refusal comes before the default delete, so nothing is left for a later save to write.
        Assert.False(await IsDeletedAsync(row, id));
        Assert.DoesNotContain(db.ChangeTracker.Entries(), entry => entry.State != EntityState.Unchanged);
    }

    [Theory]
    [InlineData(Row.AccessTree)]
    [InlineData(Row.Team)]
    public async Task Once_the_last_user_stops_using_it_the_row_can_be_deleted(Row row)
    {
        var alice = await UserAsync("alice");
        var id = await UsedByAsync(row, alice);

        await Assert.ThrowsAsync<ShiftEntityException>(() => DeleteAsync(row, id));

        await StopUsingAsync(row, id, alice);
        await DeleteAsync(row, id);

        Assert.True(await IsDeletedAsync(row, id));
    }

    [Theory]
    [InlineData(Row.AccessTree)]
    [InlineData(Row.Team)]
    public async Task Deleted_users_do_not_count(Row row)
    {
        var id = await UsedByAsync(row, await UserAsync("bob", deleted: true), await UserAsync("dave", deleted: true));

        await DeleteAsync(row, id);

        Assert.True(await IsDeletedAsync(row, id));
    }

    [Theory]
    [InlineData(Row.AccessTree)]
    [InlineData(Row.Team)]
    public async Task The_refusal_names_five_users_in_username_order_and_gives_the_total(Row row)
    {
        var users = new List<User>();
        foreach (var name in new[] { "grace", "erin", "alice", "frank", "carol", "bob", "dave" })
            users.Add(await UserAsync(name));

        var id = await UsedByAsync(row, [.. users]);

        var refusal = await RefusalAsync(row, id);

        Assert.Equal(Refusal(row, "alice, bob, carol, dave, erin", 7), refusal.Message.Body);
    }

    [Theory]
    [InlineData(Row.AccessTree, "ar-IQ")]
    [InlineData(Row.AccessTree, "ku")]
    [InlineData(Row.AccessTree, "ru")]
    [InlineData(Row.Team, "ar-IQ")]
    [InlineData(Row.Team, "ku")]
    [InlineData(Row.Team, "ru")]
    public async Task The_refusal_is_translated(Row row, string culture)
    {
        var id = await UsedByAsync(row, await UserAsync("alice"));

        var english = await RefusalAsync(row, id);
        var translated = await RefusalAsync(row, id, CultureInfo.GetCultureInfo(culture));

        Assert.NotEqual(english.Message.Body, translated.Message.Body);
        Assert.Contains("alice", translated.Message.Body);
        Assert.DoesNotContain("{0}", translated.Message.Body);
        Assert.DoesNotContain("{1}", translated.Message.Body);
    }

    // The English text, which the invariant culture gets.
    private static string Refusal(Row row, string names, int total) => row == Row.AccessTree
        ? $"Users who still have this access tree: {names} ({total} in total). Remove it from them before you delete it."
        : $"Members still in this team: {names} ({total} in total). Remove them from the team before you delete it.";

    private async Task<User> UserAsync(string name, bool active = true, bool deleted = false)
    {
        var user = new User { Username = name, FullName = name, IsActive = active, PasswordHash = [1], Salt = [2] };
        db.Users.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // An insert always saves IsDeleted as false, so a deleted user is deleted by a second save, as in the dashboard.
        if (deleted)
        {
            user.IsDeleted = true;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return user;
    }

    // The refusal of a delete, in the given culture. The invariant culture gets the English text.
    private async Task<ShiftEntityException> RefusalAsync(Row row, long id, CultureInfo? culture = null)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = culture ?? CultureInfo.InvariantCulture;

        try
        {
            return await Assert.ThrowsAsync<ShiftEntityException>(() => DeleteAsync(row, id));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    // A named tree assigned to each user, or a team with each user as a member.
    private async Task<long> UsedByAsync(Row row, params User[] users)
    {
        long id;

        if (row == Row.AccessTree)
        {
            var tree = new AccessTree { Name = "Shared", Tree = "{}" };
            db.AccessTrees.Add(tree);
            db.UserAccessTrees.AddRange(users.Select(user => new UserAccessTree { User = user, AccessTree = tree }));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            id = tree.ID;
        }
        else
        {
            var team = new Team { Name = "Ops" };
            db.Teams.Add(team);
            db.TeamUsers.AddRange(users.Select(user => new TeamUser { User = user, Team = team }));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            id = team.ID;
        }

        db.ChangeTracker.Clear();
        return id;
    }

    // Removes the assignment or the membership, as the user and team forms do.
    private async Task StopUsingAsync(Row row, long id, User user)
    {
        if (row == Row.AccessTree)
            db.UserAccessTrees.RemoveRange(db.UserAccessTrees.Where(x => x.AccessTreeID == id && x.UserID == user.ID));
        else
            db.TeamUsers.RemoveRange(db.TeamUsers.Where(x => x.TeamID == id && x.UserID == user.ID));

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
    }

    // The endpoint's delete: load the row, delete it through the built-in repository, save.
    private async Task DeleteAsync(Row row, long id)
    {
        if (row == Row.AccessTree)
        {
            var repository = new ShiftRepository<SqliteIdentityTestDbContext, AccessTree, AccessTreeListDTO, AccessTreeDTO>(db);
            await repository.DeleteAsync(await db.AccessTrees.SingleAsync(x => x.ID == id, TestContext.Current.CancellationToken), userId: 1);
        }
        else
        {
            var repository = new ShiftRepository<SqliteIdentityTestDbContext, Team, TeamListDTO, TeamDTO>(db);
            await repository.DeleteAsync(await db.Teams.SingleAsync(x => x.ID == id, TestContext.Current.CancellationToken), userId: 1);
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    // Read in a new scope, so the answer comes from the database and not from this context.
    private async Task<bool> IsDeletedAsync(Row row, long id)
    {
        await using var read = provider.CreateAsyncScope();
        var readDb = read.ServiceProvider.GetRequiredService<SqliteIdentityTestDbContext>();

        return row == Row.AccessTree
            ? await readDb.AccessTrees.Where(x => x.ID == id).Select(x => x.IsDeleted).SingleAsync(TestContext.Current.CancellationToken)
            : await readDb.Teams.Where(x => x.ID == id).Select(x => x.IsDeleted).SingleAsync(TestContext.Current.CancellationToken);
    }
}
