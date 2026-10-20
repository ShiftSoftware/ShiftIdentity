using System.Collections.Concurrent;
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.CosmosDbReplication.Services;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model.Replication;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// Pins the catch-up run on SQL Server, where the dirty-only selection is a SQL comparison of the two watermark
/// columns (on SQLite it is made in memory). A run selects the rows whose watermark is behind their save date or
/// absent, reports each row that fails, keeps that row dirty, and the next run retries it.
/// </summary>
public class ReplicationCatchUpSqlTests
{
    public sealed class Row : ShiftSoftware.ShiftEntity.Core.ShiftEntity<Row>, IShiftEntityReplication
    {
        public string Name { get; set; } = "";
        public DateTimeOffset? LastReplicationDate { get; set; }
        public string? LastReplicationStamp { get; set; }
    }

    public sealed class Db(DbContextOptions<Db> options) : ShiftDbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    public sealed class RowDocument
    {
        public string id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    private const string Container = "rows";

    // Records the name of every document it is asked to write. It refuses the ones named in refused.
    private static CosmosClient Cosmos(ConcurrentQueue<string> written, HashSet<string> refused)
    {
        var properties = Substitute.For<ContainerResponse>();
        properties.Resource.Returns(new ContainerProperties(Container, "/id"));

        var container = Substitute.For<Container>();
        container.ReadContainerAsync(Arg.Any<ContainerRequestOptions>(), Arg.Any<CancellationToken>()).Returns(properties);
        container.UpsertItemAsync(Arg.Any<RowDocument>(), Arg.Any<PartitionKey?>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var name = call.Arg<RowDocument>()!.Name;
                written.Enqueue(name);
                return refused.Contains(name)
                    ? Task.FromException<ItemResponse<RowDocument>>(
                        new CosmosException("Request rate is large.", HttpStatusCode.TooManyRequests, 3200, "activity", 0))
                    : Task.FromResult(Substitute.For<ItemResponse<RowDocument>>());
            });

        var database = Substitute.For<Microsoft.Azure.Cosmos.Database>();
        database.GetContainer(Container).Returns(container);

        var client = Substitute.For<CosmosClient>();
        client.ClientOptions.Returns(new CosmosClientOptions());
        client.GetDatabase("catch-up").Returns(database);
        return client;
    }

    [Fact]
    public async Task A_run_selects_the_dirty_rows_in_SQL_reports_the_failed_one_and_the_next_run_retries_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var catalog = await OwnedSqlCatalog.CreateAsync("ReplicationCatchUp", ct);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<Db>(o => o.UseSqlServer(catalog.ConnectionString));
        services.AddShiftEntityCosmosDbReplication<Db>();
        await using var provider = services.BuildServiceProvider(validateScopes: true);

        var saved = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Db>();
            await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(ct);
            db.Rows.AddRange(
                new Row { Name = "Never", LastSaveDate = saved },
                new Row { Name = "Refused", LastSaveDate = saved },
                new Row { Name = "Behind", LastSaveDate = saved, LastReplicationDate = saved.AddMinutes(-1) },
                new Row { Name = "Clean", LastSaveDate = saved, LastReplicationDate = saved });
            await db.SaveChangesAsync(ct);
        }

        var written = new ConcurrentQueue<string>();
        var refused = new HashSet<string> { "Refused" };
        var client = Cosmos(written, refused);

        async Task<CosmosDbReplicationResult> RunAsync()
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<CosmosDBReplication>()
                .SetUp<Db, Row>(client, "catch-up")
                .Replicate<RowDocument>(Container, x => new RowDocument { id = x.ID.ToString(), Name = x.Name })
                .RunAndReportAsync();
        }

        var first = await RunAsync();
        Assert.Equal((3, 2, 1), (first.Selected, first.Replicated, first.Failed));
        Assert.Equal(["Behind", "Never", "Refused"], written.Order());
        Assert.Equal("Refused", (await NameOfAsync(first.Failures.Single().EntityId)));

        refused.Clear();
        written.Clear();
        var second = await RunAsync();
        Assert.Equal((1, 1, 0), (second.Selected, second.Replicated, second.Failed));
        Assert.Equal(["Refused"], written);

        await using (var scope = provider.CreateAsyncScope())
        {
            var rows = await scope.ServiceProvider.GetRequiredService<Db>().Rows.AsNoTracking().ToListAsync(ct);
            Assert.All(rows, x => Assert.Equal((DateTimeOffset?)x.LastSaveDate, x.LastReplicationDate));
        }

        async Task<string> NameOfAsync(long id)
        {
            await using var scope = provider.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<Db>().Rows.AsNoTracking().SingleAsync(x => x.ID == id, ct)).Name;
        }
    }
}
