using CosmosClient = Microsoft.Azure.Cosmos.CosmosClient;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.CosmosDbReplication.Services;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.Replication;

/// <summary>
/// Reusable Cosmos "catch-up" replication for the ShiftIdentity domain — the function/timer counterpart to the
/// trigger-side <c>SetUpAllIdentityReplications</c>. Each <c>ReplicateXAsync</c> re-syncs ONE entity's rows to Cosmos
/// through the framework's <see cref="CosmosDBReplication"/> service; <see cref="ReplicateAllAsync{TDbContext}(CosmosDBReplication, string, string, bool)"/>
/// runs them all, and <see cref="ReplicateOrganizationAsync{TDbContext}(CosmosDBReplication, string, string, bool)"/> runs all but
/// users and authorization. By default only DIRTY rows are synced (<c>updateAll: false</c> — the incremental hourly catch-up); pass
/// <c>updateAll: true</c> for a full backfill (the on-demand HTTP endpoint). Each one takes the Cosmos DB connection
/// string, or a <see cref="CosmosClient"/> the host already has.
///
/// The runs of several entities go on past a step that fails, and return an <see cref="IdentityCatchUpReport"/> of
/// every step. Rows a step cannot write, for example to a container that does not exist, fail in its result and stay
/// dirty for the next run. A step that throws (its rows cannot be read, or, through a connection string, a container it
/// opens does not exist) is in the report too, and once every other step has run the run throws
/// <see cref="IdentityCatchUpException"/> with that report.
///
/// No call passes a mapping delegate: every document is mapped through the host's registered ShiftMapper mapper —
/// the same maps the trigger side uses, so a backfilled document is byte-identical to one a live save produces.
/// The Functions worker's <c>AddShiftIdentity(issuer, key)</c> and the API's <c>AddShiftIdentityDashboard</c> register
/// that mapper (the ready-made <see cref="ShiftIdentityReplicationMapper"/>), so a host that hosts identity has nothing
/// extra to wire; one that wires replication without them calls <c>AddShiftIdentityReplicationMapper()</c> itself, or
/// includes <see cref="ShiftIdentityReplicationMapper"/> in a mapper of its own. A host with neither throws out of
/// <c>RunAsync</c> before any row is touched.
///
/// It lives in ShiftIdentity.Data so both a Functions app (its timers and <c>replicate-all</c>) and an API (the
/// dashboard's <c>api/IdentityReplication</c> endpoints) can run it.
///
/// The catch-up side has no <c>IShiftEntityPrepareForReplicationAsync</c> hook (that runs only inside the save
/// trigger), so navigations the models depend on (Team's branches) are loaded HERE via the <c>SetUp(…, query)</c>
/// include shaper. A branch is loaded with its replication tree, as the save side loads it.
/// Containers/partition data are byte-identical to the trigger configuration.
/// </summary>
public static class IdentityCatchUpReplicationExtensions
{
    /// <summary>Runs every ShiftIdentity catch-up replication in dependency order, and reports each step.</summary>
    public static Task<IdentityCatchUpReport> ReplicateAllAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateAllAsync<TDbContext>(new Target(cosmos, connectionString, null, databaseId), updateAll);

    /// <summary>Runs every ShiftIdentity catch-up replication in dependency order, through a client the host already has, and reports each step.</summary>
    public static Task<IdentityCatchUpReport> ReplicateAllAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateAllAsync<TDbContext>(new Target(cosmos, null, client, databaseId), updateAll);

    private static Task<IdentityCatchUpReport> ReplicateAllAsync<TDbContext>(Target target, bool updateAll)
        where TDbContext : ShiftIdentityDbContext =>
        RunStepsAsync(target, updateAll, AllSteps<TDbContext>());

    /// <summary>
    /// Runs the catch-up of the given entities, through a client the host already has, and reports each step. They
    /// run in the order <see cref="IdentityReplicationEntities.All"/> lists them, whatever the order given, and an
    /// entity given twice runs once.
    /// </summary>
    public static Task<IdentityCatchUpReport> ReplicateAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId,
        IEnumerable<IdentityReplicationEntity> entities, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext
    {
        var selected = entities.ToHashSet();
        return RunStepsAsync(new Target(cosmos, null, client, databaseId), updateAll, [.. AllSteps<TDbContext>().Where(x => selected.Contains(x.Entity))]);
    }

    private static Step[] AllSteps<TDbContext>()
        where TDbContext : ShiftIdentityDbContext =>
        [.. OrganizationSteps<TDbContext>(), new(IdentityReplicationEntity.User, ReplicateUserAsync<TDbContext>), .. AuthorizationSteps<TDbContext>()];

    /// <summary>
    /// Runs every ShiftIdentity catch-up replication but users and authorization, in dependency order: services,
    /// departments, brands, regions, countries, cities, branches, companies and teams, and reports each step. For a
    /// host that does not replicate users.
    /// </summary>
    public static Task<IdentityCatchUpReport> ReplicateOrganizationAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateOrganizationAsync<TDbContext>(new Target(cosmos, connectionString, null, databaseId), updateAll);

    /// <inheritdoc cref="ReplicateOrganizationAsync{TDbContext}(CosmosDBReplication, string, string, bool)"/>
    public static Task<IdentityCatchUpReport> ReplicateOrganizationAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateOrganizationAsync<TDbContext>(new Target(cosmos, null, client, databaseId), updateAll);

    private static Task<IdentityCatchUpReport> ReplicateOrganizationAsync<TDbContext>(Target target, bool updateAll)
        where TDbContext : ShiftIdentityDbContext =>
        RunStepsAsync(target, updateAll, OrganizationSteps<TDbContext>());

    private static Step[] OrganizationSteps<TDbContext>()
        where TDbContext : ShiftIdentityDbContext =>
    [
        new(IdentityReplicationEntity.Service, ReplicateServiceAsync<TDbContext>),
        new(IdentityReplicationEntity.Department, ReplicateDepartmentAsync<TDbContext>),
        new(IdentityReplicationEntity.Brand, ReplicateBrandAsync<TDbContext>),
        new(IdentityReplicationEntity.Region, ReplicateRegionAsync<TDbContext>),
        new(IdentityReplicationEntity.Country, ReplicateCountryAsync<TDbContext>),
        new(IdentityReplicationEntity.City, ReplicateCityAsync<TDbContext>),
        new(IdentityReplicationEntity.CompanyBranch, ReplicateCompanyBranchAsync<TDbContext>),
        new(IdentityReplicationEntity.Company, ReplicateCompanyAsync<TDbContext>),
        new(IdentityReplicationEntity.Team, ReplicateTeamAsync<TDbContext>),
    ];

    /// <summary>
    /// Catches up named grants and assignments using the existing replication watermarks.
    /// Hard deletes are delivered by the save trigger; this scan cannot recover an already removed SQL row.
    /// Run alongside user, company and branch catch-up.
    /// </summary>
    public static Task ReplicateAuthorizationAsync<TDbContext>(this CosmosDBReplication cosmos,
        string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateAuthorizationAsync<TDbContext>(new Target(cosmos, connectionString, null, databaseId), updateAll);

    public static Task ReplicateAuthorizationAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateAuthorizationAsync<TDbContext>(new Target(cosmos, null, client, databaseId), updateAll);

    private static Task<IdentityCatchUpReport> ReplicateAuthorizationAsync<TDbContext>(Target target, bool updateAll)
        where TDbContext : ShiftIdentityDbContext =>
        RunStepsAsync(target, updateAll, AuthorizationSteps<TDbContext>());

    private static Step[] AuthorizationSteps<TDbContext>()
        where TDbContext : ShiftIdentityDbContext =>
    [
        new(IdentityReplicationEntity.AccessTree, (target, updateAll) => target.SetUp<TDbContext, AccessTree>()
            .Replicate<AccessTreeModel>(IdentityDatabaseAndContainerNames.AccessTreeContainerName).RunAndReportAsync(updateAll)),
        new(IdentityReplicationEntity.UserAccessTree, (target, updateAll) => target.SetUp<TDbContext, UserAccessTree>()
            .Replicate<UserAccessTreeModel>(IdentityDatabaseAndContainerNames.UserAccessTreeContainerName).RunAndReportAsync(updateAll)),
        new(IdentityReplicationEntity.TeamUser, (target, updateAll) => target.SetUp<TDbContext, TeamUser>()
            .Replicate<TeamUserModel>(IdentityDatabaseAndContainerNames.TeamUserContainerName).RunAndReportAsync(updateAll)),
    ];

    public static Task ReplicateServiceAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateServiceAsync<TDbContext>(new Target(cosmos, connectionString, null, databaseId), updateAll);

    public static Task ReplicateServiceAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateServiceAsync<TDbContext>(new Target(cosmos, null, client, databaseId), updateAll);

    private static Task<CosmosDbReplicationResult> ReplicateServiceAsync<TDbContext>(Target target, bool updateAll)
        where TDbContext : ShiftIdentityDbContext =>
        target.SetUp<TDbContext, Service>()
            .Replicate<ServiceModel>(IdentityDatabaseAndContainerNames.ServiceContainerName)
            .RunAndReportAsync(updateAll);

    public static Task ReplicateDepartmentAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateDepartmentAsync<TDbContext>(new Target(cosmos, connectionString, null, databaseId), updateAll);

    public static Task ReplicateDepartmentAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateDepartmentAsync<TDbContext>(new Target(cosmos, null, client, databaseId), updateAll);

    private static Task<CosmosDbReplicationResult> ReplicateDepartmentAsync<TDbContext>(Target target, bool updateAll)
        where TDbContext : ShiftIdentityDbContext =>
        target.SetUp<TDbContext, Department>()
            .Replicate<DepartmentModel>(IdentityDatabaseAndContainerNames.DepartmentContainerName)
            .RunAndReportAsync(updateAll);

    public static Task ReplicateBrandAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateBrandAsync<TDbContext>(new Target(cosmos, connectionString, null, databaseId), updateAll);

    public static Task ReplicateBrandAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateBrandAsync<TDbContext>(new Target(cosmos, null, client, databaseId), updateAll);

    private static Task<CosmosDbReplicationResult> ReplicateBrandAsync<TDbContext>(Target target, bool updateAll)
        where TDbContext : ShiftIdentityDbContext =>
        target.SetUp<TDbContext, Brand>()
            .Replicate<BrandModel>(IdentityDatabaseAndContainerNames.BrandContainerName)
            .RunAndReportAsync(updateAll);

    public static Task ReplicateRegionAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateRegionAsync<TDbContext>(new Target(cosmos, connectionString, null, databaseId), updateAll);

    public static Task ReplicateRegionAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateRegionAsync<TDbContext>(new Target(cosmos, null, client, databaseId), updateAll);

    private static Task<CosmosDbReplicationResult> ReplicateRegionAsync<TDbContext>(Target target, bool updateAll)
        where TDbContext : ShiftIdentityDbContext =>
        target.SetUp<TDbContext, Region>()
            .Replicate<RegionModel>(IdentityDatabaseAndContainerNames.CountryContainerName)
            .RunAndReportAsync(updateAll);

    public static Task ReplicateCountryAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateCountryAsync<TDbContext>(new Target(cosmos, connectionString, null, databaseId), updateAll);

    public static Task ReplicateCountryAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateCountryAsync<TDbContext>(new Target(cosmos, null, client, databaseId), updateAll);

    private static Task<CosmosDbReplicationResult> ReplicateCountryAsync<TDbContext>(Target target, bool updateAll)
        where TDbContext : ShiftIdentityDbContext =>
        target.SetUp<TDbContext, Country>()
            .Replicate<CountryModel>(IdentityDatabaseAndContainerNames.CountryContainerName)
            .RunAndReportAsync(updateAll);

    public static Task ReplicateCityAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateCityAsync<TDbContext>(new Target(cosmos, connectionString, null, databaseId), updateAll);

    public static Task ReplicateCityAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateCityAsync<TDbContext>(new Target(cosmos, null, client, databaseId), updateAll);

    private static Task<CosmosDbReplicationResult> ReplicateCityAsync<TDbContext>(Target target, bool updateAll)
        where TDbContext : ShiftIdentityDbContext =>
        target.SetUp<TDbContext, City>()
            .Replicate<CityModel>(IdentityDatabaseAndContainerNames.CountryContainerName)
            .RunAndReportAsync(updateAll);

    /// <summary>
    /// The branch's <see cref="BranchModel"/> in <c>Branches</c>, from the branch loaded with its replication tree.
    /// Nothing is written to <c>CompanyBranches</c> any more.
    /// </summary>
    public static Task ReplicateCompanyBranchAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateCompanyBranchAsync<TDbContext>(new Target(cosmos, connectionString, null, databaseId), updateAll);

    public static Task ReplicateCompanyBranchAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateCompanyBranchAsync<TDbContext>(new Target(cosmos, null, client, databaseId), updateAll);

    private static Task<CosmosDbReplicationResult> ReplicateCompanyBranchAsync<TDbContext>(Target target, bool updateAll)
        where TDbContext : ShiftIdentityDbContext =>
        target.SetUp<TDbContext, CompanyBranch>()
            .ReplicateTree<BranchModel>(IdentityDatabaseAndContainerNames.BranchContainerName)
            .RunAndReportAsync(updateAll);

    public static Task ReplicateCompanyAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateCompanyAsync<TDbContext>(new Target(cosmos, connectionString, null, databaseId), updateAll);

    public static Task ReplicateCompanyAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateCompanyAsync<TDbContext>(new Target(cosmos, null, client, databaseId), updateAll);

    private static Task<CosmosDbReplicationResult> ReplicateCompanyAsync<TDbContext>(Target target, bool updateAll)
        where TDbContext : ShiftIdentityDbContext =>
        target.SetUp<TDbContext, Company>()
            .Replicate<CompanyModel>(IdentityDatabaseAndContainerNames.CompanyContainerName)
            .RunAndReportAsync(updateAll);

    public static Task ReplicateTeamAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateTeamAsync<TDbContext>(new Target(cosmos, connectionString, null, databaseId), updateAll);

    public static Task ReplicateTeamAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateTeamAsync<TDbContext>(new Target(cosmos, null, client, databaseId), updateAll);

    private static Task<CosmosDbReplicationResult> ReplicateTeamAsync<TDbContext>(Target target, bool updateAll)
        where TDbContext : ShiftIdentityDbContext =>
        // TeamModel embeds its branches (TeamCompanyBranches → CompanyBranch) — load them so each carries a real Name.
        target.SetUp<TDbContext, Team>(q => q.Include(x => x.TeamCompanyBranches).ThenInclude(x => x.CompanyBranch))
            .Replicate<TeamModel>(IdentityDatabaseAndContainerNames.TeamContainerName)
            .RunAndReportAsync(updateAll);

    public static Task ReplicateUserAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateUserAsync<TDbContext>(new Target(cosmos, connectionString, null, databaseId), updateAll);

    public static Task ReplicateUserAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        ReplicateUserAsync<TDbContext>(new Target(cosmos, null, client, databaseId), updateAll);

    private static Task<CosmosDbReplicationResult> ReplicateUserAsync<TDbContext>(Target target, bool updateAll)
        where TDbContext : ShiftIdentityDbContext =>
        target.SetUp<TDbContext, User>()
            .Replicate<UserModel>(IdentityDatabaseAndContainerNames.UserContainerName)
            .RunAndReportAsync(updateAll);

    // One entity's catch-up in a run of several.
    private readonly record struct Step(IdentityReplicationEntity Entity, Func<Target, bool, Task<CosmosDbReplicationResult>> Run);

    // Runs the steps in order. One that fails does not stop the ones after it: the rows a step could not write fail in
    // its result and stay dirty, and a step that throws is kept with its exception. Once every step has run, a step that
    // threw makes the run throw too, with the report of every step, so it fails as loudly as it did when it stopped.
    private static async Task<IdentityCatchUpReport> RunStepsAsync(Target target, bool updateAll, Step[] steps)
    {
        var done = new List<IdentityCatchUpStep>();
        foreach (var step in steps)
        {
            try
            {
                done.Add(new IdentityCatchUpStep(step.Entity.ToString(), await step.Run(target, updateAll), null));
            }
            catch (Exception ex)
            {
                done.Add(new IdentityCatchUpStep(step.Entity.ToString(), null, ex));
            }
        }

        var report = new IdentityCatchUpReport(done);
        if (done.Any(x => x.Error is not null))
            throw new IdentityCatchUpException(report);

        return report;
    }

    // Where a catch-up writes: through the connection string, or through the host's client.
    private readonly record struct Target(CosmosDBReplication Cosmos, string? ConnectionString, CosmosClient? Client, string DatabaseId)
    {
        public CosmosDbReplicationOperation<TDbContext, TEntity> SetUp<TDbContext, TEntity>(Func<IQueryable<TEntity>, IQueryable<TEntity>>? query = null)
            where TDbContext : ShiftIdentityDbContext
            where TEntity : ShiftEntity<TEntity>, IShiftEntityReplication =>
            Client is null
                ? Cosmos.SetUp<TDbContext, TEntity>(ConnectionString!, DatabaseId, query)
                : Cosmos.SetUp<TDbContext, TEntity>(Client, DatabaseId, query);
    }
}
