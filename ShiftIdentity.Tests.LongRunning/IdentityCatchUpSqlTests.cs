using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.CosmosDbReplication.Services;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Replication;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Replication;
using Xunit;
using static ShiftSoftware.ShiftEntity.Model.Replication.IdentityDatabaseAndContainerNames;

namespace ShiftIdentity.Tests;

/// <summary>
/// The framework's Identity replication on SQL Server, through a client the host already has: the dashboard's save
/// registrations and the catch-up. A branch is written to Branches only, built from its tree, and nothing is written to
/// or read from CompanyBranches. A catch-up of several entities goes on past a step that fails, and reports every step.
/// </summary>
public class IdentityCatchUpSqlTests
{
    private const string Database = "identity";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // CompanyBranches is declared so that a write or a query there would be recorded, not refused.
    private static RecordingCosmos Cosmos(bool withBranches = true)
    {
        var cosmos = new RecordingCosmos(Database)
            .Container(CompanyBranchContainerName, "/BranchID", "/ItemType")
            .Container(CountryContainerName, "/CountryID", "/RegionID", "/ItemType")
            .Container(CompanyContainerName, "/id")
            .Container(ServiceContainerName, "/id")
            .Container(DepartmentContainerName, "/id")
            .Container(BrandContainerName, "/id")
            .Container(TeamContainerName, "/id")
            .Container(UserContainerName, "/id")
            .Container(AccessTreeContainerName, "/id")
            .Container(UserAccessTreeContainerName, "/id")
            .Container(TeamUserContainerName, "/id");
        return withBranches ? cosmos.Container(BranchContainerName, "/id") : cosmos;
    }

    private sealed class Host : IAsyncDisposable
    {
        private OwnedSqlCatalog catalog = null!;

        public RecordingCosmos Cosmos { get; private init; } = null!;
        public ReplicationSyncLog Log { get; } = new();
        public ServiceProvider Provider { get; private set; } = null!;

        // The catch-up always; the save side (SetUpOrganizationReplications) when asked.
        public static async Task<Host> StartAsync(string name, RecordingCosmos cosmos, bool saveSide = false)
        {
            var host = new Host { Cosmos = cosmos };
            host.catalog = await OwnedSqlCatalog.CreateAsync(name, Ct);

            var services = new ServiceCollection();
            services.AddLogging(x => x.AddProvider(host.Log));
            services.AddDbContext<IdentityTestDbContext>(o => o.UseSqlServer(host.catalog.ConnectionString));
            if (saveSide)
                services.AddShiftEntityCosmosDbReplicationTrigger<IdentityTestDbContext>(x =>
                    x.SetUpOrganizationReplications<IdentityTestDbContext>(cosmos.Client, Database));
            services.AddShiftEntityCosmosDbReplication<IdentityTestDbContext>();
            services.AddShiftIdentityReplicationMapper();
            host.Provider = services.BuildServiceProvider();

            await host.WithDbAsync(db => db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(Ct));
            return host;
        }

        public async Task WithDbAsync(Func<IdentityTestDbContext, Task> work)
        {
            await using var scope = Provider.CreateAsyncScope();
            await work(scope.ServiceProvider.GetRequiredService<IdentityTestDbContext>());
        }

        // A save through the context, and every sync it started.
        public async Task SaveAsync(Func<IdentityTestDbContext, Task> change)
        {
            await WithDbAsync(async db =>
            {
                await change(db);
                await db.SaveChangesAsync(Ct);
            });
            await Log.AllFinishedAsync();
        }

        public async Task<T> CatchUpAsync<T>(Func<CosmosDBReplication, Task<T>> run)
        {
            await using var scope = Provider.CreateAsyncScope();
            return await run(scope.ServiceProvider.GetRequiredService<CosmosDBReplication>());
        }

        public async ValueTask DisposeAsync()
        {
            await Log.AllFinishedAsync();
            await Provider.DisposeAsync();
            await catalog.DisposeAsync();
            Cosmos.Dispose();
        }
    }

    // A branch in Erbil, of a company, with the region's country.
    private static CompanyBranch Branch()
    {
        var region = new Region { Name = "KRG", Country = new Country { Name = "Iraq", CallingCode = "+964" } };
        return new CompanyBranch { Name = "Main", City = new City { Name = "Erbil", Region = region }, Region = region, Company = new Company { Name = "Shift" } };
    }

    private static string[] Names(JToken list) => list.Select(x => (string)x["Name"]!).ToArray();

    [Fact]
    public async Task A_save_and_a_catch_up_write_the_branch_to_Branches_and_nothing_to_CompanyBranches()
    {
        await using var host = await Host.StartAsync("IdentityBranches", Cosmos(), saveSide: true);

        var branch = Branch();
        var wash = new Service { Name = "Wash" };
        var sales = new Department { Name = "Sales" };
        var acme = new Brand { Name = "Acme" };
        await host.SaveAsync(db =>
        {
            db.AddRange(branch, wash, sales, acme);
            return Task.CompletedTask;
        });

        // The links, a rename of every part of the tree, and a deleted link. Each change rebuilds the branch's document.
        await host.SaveAsync(db =>
        {
            db.AddRange(
                new CompanyBranchService { CompanyBranchID = branch.ID, ServiceID = wash.ID },
                new CompanyBranchDepartment { CompanyBranchID = branch.ID, DepartmentID = sales.ID },
                new CompanyBranchBrand { CompanyBranchID = branch.ID, BrandID = acme.ID });
            return Task.CompletedTask;
        });
        await host.SaveAsync(async db =>
        {
            (await db.Services.SingleAsync(Ct)).Name = "Wash and Dry";
            (await db.Regions.SingleAsync(Ct)).Name = "Kurdistan Region";
            (await db.Countries.SingleAsync(Ct)).Name = "Republic of Iraq";
            (await db.Cities.SingleAsync(Ct)).Name = "Hawler";
            (await db.Companies.SingleAsync(Ct)).Name = "Shift Software";
        });
        await host.SaveAsync(async db => db.CompanyBranchBrands.Remove(await db.CompanyBranchBrands.SingleAsync(Ct)));

        Assert.Empty(host.Log.Failed);
        var saved = Assert.Single(host.Cosmos.Stored(BranchContainerName));
        Assert.Equal(["Wash and Dry"], Names(saved["Services"]!));
        Assert.Equal(["Sales"], Names(saved["Departments"]!));
        Assert.Empty(saved["Brands"]!);
        Assert.Equal("Hawler", (string?)saved["City"]!["Name"]);
        Assert.Equal("Kurdistan Region", (string?)saved["City"]!["Region"]!["Name"]);
        Assert.Equal("Republic of Iraq", (string?)saved["City"]!["Region"]!["Country"]!["Name"]);
        Assert.Equal("Shift Software", (string?)saved["Company"]!["Name"]);

        var report = await host.CatchUpAsync(x => x.ReplicateOrganizationAsync<IdentityTestDbContext>(host.Cosmos.Client, Database, updateAll: true));

        Assert.True(report.Succeeded, report.ToString());
        Assert.Equal(["Service", "Department", "Brand", "Region", "Country", "City", "CompanyBranch", "Company", "Team"],
            report.Steps.Select(x => x.Entity));
        Assert.Equal(saved.ToString(), Assert.Single(host.Cosmos.Stored(BranchContainerName)).ToString());

        Assert.DoesNotContain(host.Cosmos.Writes, x => x.Container == CompanyBranchContainerName);
        Assert.DoesNotContain(host.Cosmos.Queries, x => x.Container == CompanyBranchContainerName);
        Assert.Empty(host.Cosmos.Stored(CompanyBranchContainerName));
        Assert.Empty(host.Cosmos.Stored(UserContainerName));
    }

    // RV-19. The run used to stop at the branch step when Branches was missing, so companies and teams never ran.
    [Fact]
    public async Task An_organization_run_goes_on_past_a_step_whose_container_is_missing_and_reports_it()
    {
        await using var host = await Host.StartAsync("IdentityMissingBranches", Cosmos(withBranches: false));

        var branch = Branch();
        await host.SaveAsync(db =>
        {
            db.AddRange(branch, new Team { Name = "Ops" });
            return Task.CompletedTask;
        });

        var report = await host.CatchUpAsync(x => x.ReplicateOrganizationAsync<IdentityTestDbContext>(host.Cosmos.Client, Database, updateAll: true));

        Assert.False(report.Succeeded);
        var branches = Assert.Single(report.Steps, x => x.Entity == nameof(CompanyBranch));
        Assert.Null(branches.Error);
        Assert.Equal((1, 0, 1), (branches.Result!.Selected, branches.Result.Replicated, branches.Result.Failed));
        Assert.Equal(BranchContainerName, Assert.Single(branches.Result.Failures).ContainerId);
        Assert.All(report.Steps.Where(x => x != branches), x => Assert.True(x.Succeeded, x.ToString()));

        Assert.Single(host.Cosmos.Stored(CompanyContainerName));
        Assert.Single(host.Cosmos.Stored(TeamContainerName));
        Assert.DoesNotContain(host.Cosmos.Writes, x => x.Container == CompanyBranchContainerName);

        await host.WithDbAsync(async db =>
            Assert.Null(await db.CompanyBranches.Where(x => x.ID == branch.ID).Select(x => x.LastReplicationDate).SingleAsync(Ct)));
    }

    [Fact]
    public async Task A_step_that_throws_does_not_stop_the_steps_after_it_and_the_run_then_throws_with_its_report()
    {
        await using var host = await Host.StartAsync("IdentityThrowingStep", Cosmos());

        await host.SaveAsync(db =>
        {
            db.Users.Add(new User { Username = "catch-up", FullName = "Catch Up", PasswordHash = [1], Salt = [1], IsActive = true });
            db.AccessTrees.Add(new AccessTree { Name = "Shared", Tree = "{}" });
            return Task.CompletedTask;
        });

        // The users' step reads a column that is gone, so it throws. The three authorization steps come after it.
        await host.WithDbAsync(async db =>
        {
            var users = db.Model.FindEntityType(typeof(User))!;
            var table = $"{users.GetSchema() ?? "dbo"}.{users.GetTableName()}";
            await db.Database.ExecuteSqlRawAsync($"EXEC sp_rename '{table}.FullName', 'FullNameGone', 'COLUMN'", Ct);
        });

        var thrown = await Assert.ThrowsAsync<IdentityCatchUpException>(() =>
            host.CatchUpAsync(x => x.ReplicateAllAsync<IdentityTestDbContext>(host.Cosmos.Client, Database, updateAll: true)));

        var steps = thrown.Report.Steps;
        Assert.Equal(13, steps.Count);
        var users = Assert.Single(steps, x => x.Entity == nameof(User));
        Assert.Null(users.Result);
        Assert.Same(users.Error, thrown.InnerException);
        Assert.Contains("FullName", users.Error!.Message);

        Assert.Equal([nameof(AccessTree), nameof(UserAccessTree), nameof(TeamUser)],
            steps.SkipWhile(x => x != users).Skip(1).Select(x => x.Entity));
        Assert.All(steps.Where(x => x != users), x => Assert.True(x.Succeeded, x.ToString()));
        Assert.Equal(1, steps.Single(x => x.Entity == nameof(AccessTree)).Result!.Replicated);
        Assert.Single(host.Cosmos.Stored(AccessTreeContainerName));
        Assert.Empty(host.Cosmos.Stored(UserContainerName));
    }
}
