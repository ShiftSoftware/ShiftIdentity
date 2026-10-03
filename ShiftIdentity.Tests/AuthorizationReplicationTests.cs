using System.Collections.Concurrent;
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Replication;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Replication;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>Runs Identity's actual authorization registration and mapper through the real save trigger.
/// SQLite stores the three participating entities; Cosmos is an in-process stand-in.</summary>
public class AuthorizationReplicationTests
{
    public sealed class ReplicationDb(DbContextOptions<ReplicationDb> options) : ShiftIdentityDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            // The Identity SQL Server model is not under test here. Keep the actual scalar join entities
            // and replication columns while leaving SQL Server-specific computed columns out of SQLite.
            foreach (var entity in model.Model.GetEntityTypes().ToArray()) model.Ignore(entity.ClrType);
            model.Entity<AccessTree>();
            model.Entity<UserAccessTree>().Ignore(x => x.User).Ignore(x => x.AccessTree);
            model.Entity<TeamUser>().Ignore(x => x.User).Ignore(x => x.Team);
        }
    }

    private sealed class CompletionLog : ILoggerProvider, ILogger
    {
        private readonly SemaphoreSlim completed = new(0);
        public ConcurrentQueue<string> Errors { get; } = new();
        public async Task WaitAsync()
        {
            Assert.True(await completed.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Empty(Errors);
        }
        public ILogger CreateLogger(string category) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> format)
        {
            var text = format(state, exception);
            if (text.StartsWith("CosmosDB Syncing Failed")) { Errors.Enqueue(text); completed.Release(); }
            else if (text.StartsWith("CosmosDB Syncing Succeeded")) completed.Release();
        }
        public void Dispose() => completed.Dispose();
    }

    [Fact]
    public async Task Named_tree_edits_and_hard_deleted_assignments_and_memberships_reach_Cosmos()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var stored = new ConcurrentDictionary<string, ReplicationModel>();
        var database = Substitute.For<Database>();
        Wire<AccessTreeModel>(database, IdentityDatabaseAndContainerNames.AccessTreeContainerName, stored);
        Wire<UserAccessTreeModel>(database, IdentityDatabaseAndContainerNames.UserAccessTreeContainerName, stored);
        Wire<TeamUserModel>(database, IdentityDatabaseAndContainerNames.TeamUserContainerName, stored);
        var client = Substitute.For<CosmosClient>();
        client.ClientOptions.Returns(new CosmosClientOptions());
        client.GetDatabase("Identity").Returns(database);

        var log = new CompletionLog();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(log));
        services.AddDbContext<ReplicationDb>(options => options.UseSqlite(connection));
        services.AddShiftIdentityReplicationMapper();
        services.AddShiftEntityCosmosDbReplicationTrigger<ReplicationDb>(options =>
            options.SetUpAuthorizationReplication<ReplicationDb>(client, "Identity"));
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReplicationDb>();
        var ct = TestContext.Current.CancellationToken;
        await db.Database.EnsureCreatedAsync(ct);

        var tree = new AccessTree { Name = "Shared", Tree = "{\"grant\":true}" };
        db.AccessTrees.Add(tree);
        await db.SaveChangesAsync(ct);
        await log.WaitAsync();
        tree.Tree = "{}";
        await db.SaveChangesAsync(ct);
        await log.WaitAsync();
        Assert.Equal("{}", Assert.IsType<AccessTreeModel>(stored["AccessTrees/" + tree.ID]).Tree);

        var assignment = new UserAccessTree { UserID = 5, AccessTreeID = tree.ID };
        db.UserAccessTrees.Add(assignment);
        await db.SaveChangesAsync(ct);
        await log.WaitAsync();
        Assert.Equal(tree.ID, Assert.IsType<UserAccessTreeModel>(stored["UserAccessTrees/" + assignment.ID]).AccessTreeID);
        db.ChangeTracker.Clear();
        var reloaded = await db.UserAccessTrees.SingleAsync(ct);
        Assert.NotNull(reloaded.LastReplicationStamp);
        db.UserAccessTrees.Remove(reloaded);
        await db.SaveChangesAsync(ct);
        await log.WaitAsync();
        Assert.DoesNotContain("UserAccessTrees/" + assignment.ID, stored.Keys);

        var membership = new TeamUser { UserID = 5, TeamID = 9 };
        db.TeamUsers.Add(membership);
        await db.SaveChangesAsync(ct);
        await log.WaitAsync();
        Assert.Equal(9, Assert.IsType<TeamUserModel>(stored["TeamUsers/" + membership.ID]).TeamID);
        db.ChangeTracker.Clear();
        var reloadedMembership = await db.TeamUsers.SingleAsync(ct);
        db.TeamUsers.Remove(reloadedMembership);
        await db.SaveChangesAsync(ct);
        await log.WaitAsync();
        Assert.DoesNotContain("TeamUsers/" + membership.ID, stored.Keys);
    }

    private static void Wire<T>(Database database, string name, ConcurrentDictionary<string, ReplicationModel> stored)
        where T : ReplicationModel
    {
        var properties = Substitute.For<ContainerResponse>();
        properties.Resource.Returns(new ContainerProperties(name, "/id"));
        var container = Substitute.For<Container>();
        container.ReadContainerAsync(Arg.Any<ContainerRequestOptions>(), Arg.Any<CancellationToken>()).Returns(properties);
        container.UpsertItemAsync(Arg.Any<T>(), Arg.Any<PartitionKey?>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var item = call.Arg<T>();
                stored[name + "/" + item.id] = item;
                return Response<T>(HttpStatusCode.OK);
            });
        container.DeleteItemAsync<T>(Arg.Any<string>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Assert.Equal(new PartitionKey(call.Arg<string>()), call.Arg<PartitionKey>());
                stored.TryRemove(name + "/" + call.Arg<string>(), out _);
                return Response<T>(HttpStatusCode.NoContent);
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
