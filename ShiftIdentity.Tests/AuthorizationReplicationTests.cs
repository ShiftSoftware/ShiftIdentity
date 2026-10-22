using System.Collections.Concurrent;
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.CosmosDbReplication.Services;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.AzureFunctions.Replication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Replication;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Replication;
using ShiftSoftware.ShiftIdentity.Data.Repositories;
using ShiftIdentity.Tests.Infrastructure;
using Xunit;
// The Cosmos namespace has a User type as well.
using User = ShiftSoftware.ShiftIdentity.Data.Entities.User;

namespace ShiftIdentity.Tests;

/// <summary>
/// Runs Identity's own replication wiring, preparers and mapper through the real save trigger and the real catch-up.
/// SQLite stores the identity model; Cosmos is an in-process stand-in that can refuse one container's writes.
/// <para>
/// The user and team documents carry the assignment and membership lists (<see cref="UserModel.AccessTreeIDs"/>,
/// <see cref="TeamModel.UserIDs"/>). A removal hard-deletes the join row, and the same save saves the user or team row.
/// So when the sync of that save fails, the row stays dirty, and the next catch-up rewrites the whole list.
/// </para>
/// </summary>
public sealed class AuthorizationReplicationTests : IAsyncLifetime
{
    private const string DatabaseId = "Identity";

    private sealed class CompletionLog : ILoggerProvider, ILogger
    {
        private readonly SemaphoreSlim completed = new(0);
        private readonly ConcurrentQueue<string> errors = new();

        // Waits until the sync that one save started has finished, and checks whether it failed.
        public async Task WaitAsync(bool failed)
        {
            Assert.True(await completed.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Equal(failed ? 1 : 0, errors.Count);
            errors.Clear();
        }

        public ILogger CreateLogger(string category) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> format)
        {
            var text = format(state, exception);
            if (text.StartsWith("CosmosDB Syncing Failed")) { errors.Enqueue(text); completed.Release(); }
            else if (text.StartsWith("CosmosDB Syncing Succeeded")) completed.Release();
        }
        public void Dispose() => completed.Dispose();
    }

    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ConcurrentDictionary<string, ReplicationModel> stored = new();
    // The containers whose writes Cosmos refuses, as a throttled container does once the SDK has used up its retries.
    private readonly ConcurrentDictionary<string, bool> refused = new();
    private readonly CompletionLog log = new();
    private readonly CosmosClient client;
    private readonly ServiceProvider provider;
    private AsyncServiceScope scope;
    private SqliteIdentityTestDbContext db = null!;

    public AuthorizationReplicationTests()
    {
        var database = Substitute.For<Database>();
        Wire<UserModel>(database, IdentityDatabaseAndContainerNames.UserContainerName);
        Wire<TeamModel>(database, IdentityDatabaseAndContainerNames.TeamContainerName);
        Wire<AccessTreeModel>(database, IdentityDatabaseAndContainerNames.AccessTreeContainerName);
        client = Substitute.For<CosmosClient>();
        client.ClientOptions.Returns(new CosmosClientOptions());
        client.GetDatabase(DatabaseId).Returns(database);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(log));
        services.AddDbContext<SqliteIdentityTestDbContext>(options => options.UseSqlite(connection));
        services.AddScoped<ShiftIdentityDbContext>(sp => sp.GetRequiredService<SqliteIdentityTestDbContext>());
        // The two preparers AddShiftIdentityDashboard registers for these entities (see the registration test below).
        services.AddScoped<IShiftEntityPrepareForReplicationAsync<User>, UserReplicationPreparer>();
        services.AddScoped<IShiftEntityPrepareForReplicationAsync<Team>, TeamReplicationPreparer>();
        services.AddShiftIdentityReplicationMapper();
        services.AddShiftEntityCosmosDbReplicationTrigger<SqliteIdentityTestDbContext>(options =>
            options.SetUpAllIdentityReplications<SqliteIdentityTestDbContext>(client, DatabaseId));
        services.AddShiftEntityCosmosDbReplication<SqliteIdentityTestDbContext>();
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
        log.Dispose();
    }

    [Fact]
    public async Task A_user_save_lists_the_assigned_trees_in_assignment_order_and_a_removal_drops_one()
    {
        var (low, high) = await TwoTreesAsync();

        // The higher tree is assigned first. The document follows the assignments, not the tree IDs: that is the order
        // sign-in adds the trees to the session.
        var user = NewUser("assigned");
        user.AccessTrees = new List<UserAccessTree> { new() { AccessTreeID = high.ID }, new() { AccessTreeID = low.ID } };
        db.Users.Add(user);
        await SaveAsync();
        Assert.Equal([high.ID, low.ID], Document<UserModel>(IdentityDatabaseAndContainerNames.UserContainerName, user.ID).AccessTreeIDs);

        await RemoveAssignmentAsync(user, high.ID);
        Assert.Equal([low.ID], Document<UserModel>(IdentityDatabaseAndContainerNames.UserContainerName, user.ID).AccessTreeIDs);
        await AssertReplicatedAsync<User>(user.ID);
    }

    // A sign-in or a password change saves the user row without loading the assignments. The preparer loads them by
    // query, so the document stays complete.
    [Fact]
    public async Task A_user_save_that_did_not_load_the_assignments_still_lists_them_all()
    {
        var (low, high) = await TwoTreesAsync();
        var user = NewUser("signed-in");
        user.AccessTrees = new List<UserAccessTree> { new() { AccessTreeID = low.ID } };
        db.Users.Add(user);
        await SaveAsync();

        await using (var other = provider.CreateAsyncScope())
        {
            var otherDb = other.ServiceProvider.GetRequiredService<SqliteIdentityTestDbContext>();
            otherDb.UserAccessTrees.Add(new UserAccessTree { UserID = user.ID, AccessTreeID = high.ID });
            await otherDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var signIn = provider.CreateAsyncScope())
        {
            var signInDb = signIn.ServiceProvider.GetRequiredService<SqliteIdentityTestDbContext>();
            var loaded = await signInDb.Users.SingleAsync(x => x.ID == user.ID, TestContext.Current.CancellationToken);
            Assert.Empty(loaded.AccessTrees);
            loaded.LoginAttempts++;
            await signInDb.SaveChangesAsync(TestContext.Current.CancellationToken);
            await log.WaitAsync(failed: false);
        }

        Assert.Equal([low.ID, high.ID], Document<UserModel>(IdentityDatabaseAndContainerNames.UserContainerName, user.ID).AccessTreeIDs);
    }

    [Fact]
    public async Task A_failed_user_sync_leaves_the_row_dirty_and_the_catch_up_rewrites_the_list()
    {
        var (low, high) = await TwoTreesAsync();
        var user = NewUser("revoked");
        user.AccessTrees = new List<UserAccessTree> { new() { AccessTreeID = low.ID }, new() { AccessTreeID = high.ID } };
        db.Users.Add(user);
        await SaveAsync();

        refused[IdentityDatabaseAndContainerNames.UserContainerName] = true;
        await RemoveAssignmentAsync(user, high.ID, failed: true);

        // The document still lists the removed tree, and the row says so: it is behind its last save.
        Assert.Equal([low.ID, high.ID], Document<UserModel>(IdentityDatabaseAndContainerNames.UserContainerName, user.ID).AccessTreeIDs);
        var row = await RowAsync<User>(user.ID);
        Assert.True(row.LastReplicationDate < row.LastSaveDate);

        refused.Clear();
        await CatchUpAsync(cosmos => cosmos.ReplicateUserAsync<SqliteIdentityTestDbContext>(client, DatabaseId));
        Assert.Equal([low.ID], Document<UserModel>(IdentityDatabaseAndContainerNames.UserContainerName, user.ID).AccessTreeIDs);
        await AssertReplicatedAsync<User>(user.ID);
    }

    [Fact]
    public async Task A_team_save_lists_the_members_in_ascending_order_and_a_removal_drops_one()
    {
        var (first, _, third) = await ThreeUsersAsync();

        // The later user joins first, so the join rows' own order is the opposite of the user order.
        var team = new Team { Name = "Ops", TeamUsers = new List<TeamUser> { new() { UserID = third.ID }, new() { UserID = first.ID } } };
        db.Teams.Add(team);
        await SaveAsync();
        Assert.Equal([first.ID, third.ID], Document<TeamModel>(IdentityDatabaseAndContainerNames.TeamContainerName, team.ID).UserIDs);

        await RemoveMemberAsync(team, third.ID);
        Assert.Equal([first.ID], Document<TeamModel>(IdentityDatabaseAndContainerNames.TeamContainerName, team.ID).UserIDs);
        await AssertReplicatedAsync<Team>(team.ID);
    }

    [Fact]
    public async Task A_team_save_that_did_not_load_the_members_still_lists_them_all()
    {
        var (first, second, _) = await ThreeUsersAsync();
        var team = new Team { Name = "Ops", TeamUsers = new List<TeamUser> { new() { UserID = first.ID } } };
        db.Teams.Add(team);
        await SaveAsync();

        await using (var other = provider.CreateAsyncScope())
        {
            var otherDb = other.ServiceProvider.GetRequiredService<SqliteIdentityTestDbContext>();
            otherDb.TeamUsers.Add(new TeamUser { UserID = second.ID, TeamID = team.ID });
            await otherDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var rename = provider.CreateAsyncScope())
        {
            var renameDb = rename.ServiceProvider.GetRequiredService<SqliteIdentityTestDbContext>();
            var loaded = await renameDb.Teams.SingleAsync(x => x.ID == team.ID, TestContext.Current.CancellationToken);
            Assert.Empty(loaded.TeamUsers);
            loaded.Name = "Operations";
            await renameDb.SaveChangesAsync(TestContext.Current.CancellationToken);
            await log.WaitAsync(failed: false);
        }

        Assert.Equal([first.ID, second.ID], Document<TeamModel>(IdentityDatabaseAndContainerNames.TeamContainerName, team.ID).UserIDs);
    }

    [Fact]
    public async Task A_failed_team_sync_leaves_the_row_dirty_and_the_catch_up_rewrites_the_list()
    {
        var (first, second, _) = await ThreeUsersAsync();
        var team = new Team { Name = "Ops", TeamUsers = new List<TeamUser> { new() { UserID = first.ID }, new() { UserID = second.ID } } };
        db.Teams.Add(team);
        await SaveAsync();

        refused[IdentityDatabaseAndContainerNames.TeamContainerName] = true;
        await RemoveMemberAsync(team, second.ID, failed: true);

        Assert.Equal([first.ID, second.ID], Document<TeamModel>(IdentityDatabaseAndContainerNames.TeamContainerName, team.ID).UserIDs);
        var row = await RowAsync<Team>(team.ID);
        Assert.True(row.LastReplicationDate < row.LastSaveDate);

        refused.Clear();
        await CatchUpAsync(cosmos => cosmos.ReplicateTeamAsync<SqliteIdentityTestDbContext>(client, DatabaseId));
        Assert.Equal([first.ID], Document<TeamModel>(IdentityDatabaseAndContainerNames.TeamContainerName, team.ID).UserIDs);
        await AssertReplicatedAsync<Team>(team.ID);
    }

    [Fact]
    public async Task Named_tree_edits_and_deletes_reach_Cosmos()
    {
        var tree = new AccessTree { Name = "Shared", Tree = "{\"grant\":true}" };
        db.AccessTrees.Add(tree);
        await SaveAsync();
        tree.Tree = "{}";
        await SaveAsync();
        Assert.Equal("{}", Document<AccessTreeModel>(IdentityDatabaseAndContainerNames.AccessTreeContainerName, tree.ID).Tree);

        // Named trees are soft-deleted, so a delete is one more edit of the same document.
        tree.IsDeleted = true;
        await SaveAsync();
        Assert.True(Document<AccessTreeModel>(IdentityDatabaseAndContainerNames.AccessTreeContainerName, tree.ID).IsDeleted);
        await AssertReplicatedAsync<AccessTree>(tree.ID);
    }

    // Every repository is also registered as the replication preparer of its entity, and the save trigger gets the last
    // registration. A registration that keeps the interface's default prepares nothing, and the document would then
    // list only what the save happened to load. So every User and Team preparer the dashboard registers must have its
    // own implementation.
    [Fact]
    public void Every_user_and_team_preparer_the_dashboard_registers_loads_the_lists_itself()
    {
        var services = new ServiceCollection();
        services.AddControllers().AddShiftIdentityDashboard<SqliteIdentityTestDbContext>(new ShiftIdentityConfiguration());

        foreach (var entity in new[] { typeof(User), typeof(Team) })
        {
            var preparer = typeof(IShiftEntityPrepareForReplicationAsync<>).MakeGenericType(entity);
            var implementations = services.Where(x => x.ServiceType == preparer).Select(x => x.ImplementationType!).Distinct().ToList();

            Assert.NotEmpty(implementations);
            Assert.All(implementations, implementation =>
                Assert.All(implementation.GetInterfaceMap(preparer).TargetMethods, method => Assert.NotEqual(preparer, method.DeclaringType)));
        }
    }

    private static User NewUser(string name) => new()
    {
        Username = name, FullName = name, IsActive = true, PasswordHash = [1], Salt = [2],
    };

    // Rows are saved one at a time. Each save starts a sync in the background, and the syncs of one save would run at
    // the same time on the one SQLite connection, which cannot be shared that way.
    private async Task<(AccessTree Low, AccessTree High)> TwoTreesAsync()
    {
        var low = new AccessTree { Name = "Low", Tree = "{}" };
        db.AccessTrees.Add(low);
        await SaveAsync();
        var high = new AccessTree { Name = "High", Tree = "{}" };
        db.AccessTrees.Add(high);
        await SaveAsync();
        return (low, high);
    }

    private async Task<(User First, User Second, User Third)> ThreeUsersAsync()
    {
        var users = new[] { NewUser("first"), NewUser("second"), NewUser("third") };
        foreach (var user in users)
        {
            db.Users.Add(user);
            await SaveAsync();
        }
        return (users[0], users[1], users[2]);
    }

    // The user repository saves the user row with every change to its assignments (UserFormLegacySqlTests pins that).
    // A save straight through the context does not, so these tests stamp the row the same way.
    private async Task RemoveAssignmentAsync(User user, long accessTreeId, bool failed = false)
    {
        db.UserAccessTrees.Remove(user.AccessTrees.Single(x => x.AccessTreeID == accessTreeId));
        user.LastSaveDate = DateTimeOffset.UtcNow;
        await SaveAsync(failed);
    }

    // The team's upsert hook saves the team row with every change to its members; this stamps it the same way.
    private async Task RemoveMemberAsync(Team team, long userId, bool failed = false)
    {
        db.TeamUsers.Remove(team.TeamUsers.Single(x => x.UserID == userId));
        team.LastSaveDate = DateTimeOffset.UtcNow;
        await SaveAsync(failed);
    }

    private async Task SaveAsync(bool failed = false)
    {
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await log.WaitAsync(failed);
    }

    private async Task CatchUpAsync(Func<CosmosDBReplication, Task> catchUp)
    {
        await using var catchUpScope = provider.CreateAsyncScope();
        await catchUp(catchUpScope.ServiceProvider.GetRequiredService<CosmosDBReplication>());
    }

    private T Document<T>(string container, long id) where T : ReplicationModel
        => Assert.IsType<T>(stored[container + "/" + id]);

    private async Task<T> RowAsync<T>(long id) where T : ShiftEntity<T>, IShiftEntityReplication
    {
        await using var readScope = provider.CreateAsyncScope();
        return await readScope.ServiceProvider.GetRequiredService<SqliteIdentityTestDbContext>().Set<T>().AsNoTracking()
            .SingleAsync(x => x.ID == id, TestContext.Current.CancellationToken);
    }

    // In sync: the watermark holds the save date of the version that was replicated last.
    private async Task AssertReplicatedAsync<T>(long id) where T : ShiftEntity<T>, IShiftEntityReplication
    {
        var row = await RowAsync<T>(id);
        Assert.Equal(row.LastSaveDate, row.LastReplicationDate);
    }

    private void Wire<T>(Database database, string name) where T : ReplicationModel
    {
        var properties = Substitute.For<ContainerResponse>();
        properties.Resource.Returns(new ContainerProperties(name, "/id"));
        var container = Substitute.For<Container>();
        container.ReadContainerAsync(Arg.Any<ContainerRequestOptions>(), Arg.Any<CancellationToken>()).Returns(properties);
        container.UpsertItemAsync(Arg.Any<T>(), Arg.Any<PartitionKey?>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (refused.ContainsKey(name))
                    return Task.FromException<ItemResponse<T>>(
                        new CosmosException("Request rate is large.", HttpStatusCode.TooManyRequests, 3200, "activity", 0));
                var item = call.Arg<T>();
                stored[name + "/" + item.id] = item;
                return Task.FromResult(Response<T>(HttpStatusCode.OK));
            });
        container.DeleteItemAsync<T>(Arg.Any<string>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                stored.TryRemove(name + "/" + call.Arg<string>(), out _);
                return Task.FromResult(Response<T>(HttpStatusCode.NoContent));
            });
        database.GetContainer(name).Returns(container);
    }

    private static ItemResponse<T> Response<T>(HttpStatusCode status)
    {
        var response = Substitute.For<ItemResponse<T>>();
        response.StatusCode.Returns(status);
        return response;
    }
}
