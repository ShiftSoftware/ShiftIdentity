using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.CosmosDbReplication.Services;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Replication;
using System;
using System.Linq;
using System.Threading.Tasks;
// Only the client: the Cosmos namespace also has a User type, which would clash with the identity entity.
using CosmosClient = Microsoft.Azure.Cosmos.CosmosClient;

namespace ShiftSoftware.ShiftIdentity.AzureFunctions.Replication;

/// <summary>
/// Reusable Cosmos "catch-up" replication for the ShiftIdentity domain — the function/timer counterpart to the
/// trigger-side <c>SetUpAllIdentityReplications</c>. Each <c>ReplicateXAsync</c> re-syncs ONE entity's rows to Cosmos
/// through the framework's <see cref="CosmosDBReplication"/> service; <see cref="ReplicateAllAsync{TDbContext}(CosmosDBReplication, CosmosClient, string, bool)"/>
/// runs them all. By default only DIRTY rows are synced (<c>updateAll: false</c> — the incremental hourly catch-up);
/// pass <c>updateAll: true</c> for a full backfill (the on-demand HTTP endpoint).
///
/// Every method comes in two forms. The <see cref="CosmosClient"/> form uses a client the host already holds. The
/// connection-string form opens one client for the call, runs the client form with it, and closes it at the end.
///
/// No call passes a mapping delegate: every document is mapped through the host's registered ShiftMapper mapper —
/// the same maps the trigger side uses, so a backfilled document is byte-identical to one a live save produces.
/// The Functions worker's <c>AddShiftIdentity(issuer, key)</c> registers that mapper (the ready-made
/// <see cref="ShiftIdentityReplicationMapper"/>), so a host that hosts identity has nothing extra to wire; one that
/// wires replication without it calls <c>AddShiftIdentityReplicationMapper()</c> itself, or includes
/// <see cref="ShiftIdentityReplicationMapper"/> in a mapper of its own. A host with neither throws out of <c>RunAsync</c> before
/// any row is touched.
///
/// The catch-up side has no <c>IShiftEntityPrepareForReplicationAsync</c> hook (that runs only inside the save
/// trigger), so navigations the models depend on (City→Region→Country, CompanyBranch's City/Company, the M:N joins'
/// Service/Department/Brand, Team's branches and members, User's assigned trees) are loaded HERE via the
/// <c>SetUp(…, query)</c> include shaper. A host that writes its own user or team catch-up must load the assigned trees
/// or the members the same way, or the document gets an empty list.
///
/// The user and team documents carry the assignment and membership lists. Removing an assignment or a membership
/// hard-deletes the join row, but the same save also saves the user or team row, so the row stays dirty until a sync
/// succeeds, and the catch-up rewrites the whole list.
/// Containers/partition data are byte-identical to the trigger configuration.
/// </summary>
public static class IdentityCatchUpReplicationExtensions
{
    /// <summary>Runs every ShiftIdentity catch-up replication in dependency order.</summary>
    public static Task ReplicateAllAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        WithClientAsync(connectionString, client => cosmos.ReplicateAllAsync<TDbContext>(client, databaseId, updateAll));

    /// <summary>Runs every ShiftIdentity catch-up replication in dependency order.</summary>
    public static async Task ReplicateAllAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext
    {
        await cosmos.ReplicateServiceAsync<TDbContext>(client, databaseId, updateAll);
        await cosmos.ReplicateCompanyBranchServiceAsync<TDbContext>(client, databaseId, updateAll);
        await cosmos.ReplicateCompanyBranchDepartmentAsync<TDbContext>(client, databaseId, updateAll);
        await cosmos.ReplicateDepartmentAsync<TDbContext>(client, databaseId, updateAll);
        await cosmos.ReplicateBrandAsync<TDbContext>(client, databaseId, updateAll);
        await cosmos.ReplicateCompanyBranchBrandAsync<TDbContext>(client, databaseId, updateAll);
        await cosmos.ReplicateRegionAsync<TDbContext>(client, databaseId, updateAll);
        await cosmos.ReplicateCountryAsync<TDbContext>(client, databaseId, updateAll);
        await cosmos.ReplicateCityAsync<TDbContext>(client, databaseId, updateAll);
        await cosmos.ReplicateCompanyBranchAsync<TDbContext>(client, databaseId, updateAll);
        await cosmos.ReplicateCompanyAsync<TDbContext>(client, databaseId, updateAll);
        await cosmos.ReplicateTeamAsync<TDbContext>(client, databaseId, updateAll);
        await cosmos.ReplicateUserAsync<TDbContext>(client, databaseId, updateAll);
        await cosmos.ReplicateAccessTreeAsync<TDbContext>(client, databaseId, updateAll);
    }

    /// <summary>
    /// Catches up the named access trees using the existing replication watermarks. Trees are soft-deleted, so a
    /// deleted tree is rewritten with <c>IsDeleted</c> like any other edit. Which trees a user has is the list on the
    /// user document, which <see cref="ReplicateUserAsync{TDbContext}(CosmosDBReplication, CosmosClient, string, bool)"/>
    /// catches up.
    /// </summary>
    public static Task ReplicateAccessTreeAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        WithClientAsync(connectionString, client => cosmos.ReplicateAccessTreeAsync<TDbContext>(client, databaseId, updateAll));

    /// <inheritdoc cref="ReplicateAccessTreeAsync{TDbContext}(CosmosDBReplication, string, string, bool)"/>
    public static Task ReplicateAccessTreeAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        cosmos.SetUp<TDbContext, AccessTree>(client, databaseId)
            .Replicate<AccessTreeModel>(IdentityDatabaseAndContainerNames.AccessTreeContainerName)
            .RunAsync(updateAll);

    public static Task ReplicateServiceAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        WithClientAsync(connectionString, client => cosmos.ReplicateServiceAsync<TDbContext>(client, databaseId, updateAll));

    public static Task ReplicateServiceAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        cosmos.SetUp<TDbContext, Service>(client, databaseId)
            .Replicate<ServiceModel>(IdentityDatabaseAndContainerNames.ServiceContainerName)
            .UpdateReference<CompanyBranchSubItemModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName,
                (q, e) => q.Where(m => m.ItemType == CompanyBranchContainerItemTypes.Service && m.id == e.ID.ToString()))
            .RunAsync(updateAll);

    public static Task ReplicateCompanyBranchServiceAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        WithClientAsync(connectionString, client => cosmos.ReplicateCompanyBranchServiceAsync<TDbContext>(client, databaseId, updateAll));

    public static Task ReplicateCompanyBranchServiceAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        // Include Service — the sub-item carries its Name/IntegrationId (null on the bare join otherwise).
        cosmos.SetUp<TDbContext, CompanyBranchService>(client, databaseId, q => q.Include(x => x.Service))
            .Replicate<CompanyBranchSubItemModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName)
            .RunAsync(updateAll);

    public static Task ReplicateCompanyBranchDepartmentAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        WithClientAsync(connectionString, client => cosmos.ReplicateCompanyBranchDepartmentAsync<TDbContext>(client, databaseId, updateAll));

    public static Task ReplicateCompanyBranchDepartmentAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        cosmos.SetUp<TDbContext, CompanyBranchDepartment>(client, databaseId, q => q.Include(x => x.Department))
            .Replicate<CompanyBranchSubItemModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName)
            .RunAsync(updateAll);

    public static Task ReplicateDepartmentAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        WithClientAsync(connectionString, client => cosmos.ReplicateDepartmentAsync<TDbContext>(client, databaseId, updateAll));

    public static Task ReplicateDepartmentAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        cosmos.SetUp<TDbContext, Department>(client, databaseId)
            .Replicate<DepartmentModel>(IdentityDatabaseAndContainerNames.DepartmentContainerName)
            .UpdateReference<CompanyBranchSubItemModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName,
                (q, e) => q.Where(m => m.ItemType == CompanyBranchContainerItemTypes.Department && m.id == e.ID.ToString()))
            .RunAsync(updateAll);

    public static Task ReplicateBrandAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        WithClientAsync(connectionString, client => cosmos.ReplicateBrandAsync<TDbContext>(client, databaseId, updateAll));

    public static Task ReplicateBrandAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        cosmos.SetUp<TDbContext, Brand>(client, databaseId)
            .Replicate<BrandModel>(IdentityDatabaseAndContainerNames.BrandContainerName)
            .UpdateReference<CompanyBranchSubItemModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName,
                (q, e) => q.Where(m => m.id == e.ID.ToString() && m.ItemType == CompanyBranchContainerItemTypes.Brand))
            .RunAsync(updateAll);

    public static Task ReplicateCompanyBranchBrandAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        WithClientAsync(connectionString, client => cosmos.ReplicateCompanyBranchBrandAsync<TDbContext>(client, databaseId, updateAll));

    public static Task ReplicateCompanyBranchBrandAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        cosmos.SetUp<TDbContext, CompanyBranchBrand>(client, databaseId, q => q.Include(x => x.Brand))
            .Replicate<CompanyBranchSubItemModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName)
            .RunAsync(updateAll);

    public static Task ReplicateRegionAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        WithClientAsync(connectionString, client => cosmos.ReplicateRegionAsync<TDbContext>(client, databaseId, updateAll));

    public static Task ReplicateRegionAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        // Include Country — the nested CityRegionModel.Country reference is built from it.
        cosmos.SetUp<TDbContext, Region>(client, databaseId, q => q.Include(x => x.Country))
            .Replicate<RegionModel>(IdentityDatabaseAndContainerNames.CountryContainerName)
            .UpdatePropertyReference<CityRegionModel, CompanyBranchModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName, m => m.City.Region,
                (q, e) => q.Where(m => m.City.Region.id == e.ID.ToString() && m.ItemType == CompanyBranchContainerItemTypes.Branch))
            .RunAsync(updateAll);

    public static Task ReplicateCountryAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        WithClientAsync(connectionString, client => cosmos.ReplicateCountryAsync<TDbContext>(client, databaseId, updateAll));

    public static Task ReplicateCountryAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        cosmos.SetUp<TDbContext, Country>(client, databaseId)
            .Replicate<CountryModel>(IdentityDatabaseAndContainerNames.CountryContainerName)
            .UpdatePropertyReference<CountryModel, CompanyBranchModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName, m => m.City.Region.Country,
                (q, e) => q.Where(m => m.City.Region.Country.id == e.ID.ToString() && m.ItemType == CompanyBranchContainerItemTypes.Branch))
            .RunAsync(updateAll);

    public static Task ReplicateCityAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        WithClientAsync(connectionString, client => cosmos.ReplicateCityAsync<TDbContext>(client, databaseId, updateAll));

    public static Task ReplicateCityAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        // Include Region→Country — the nested CityCompanyBranchModel.Region (and its Country) are built from them.
        cosmos.SetUp<TDbContext, City>(client, databaseId, q => q.Include(x => x.Region).ThenInclude(x => x.Country))
            .Replicate<CityModel>(IdentityDatabaseAndContainerNames.CountryContainerName)
            .UpdatePropertyReference<CityCompanyBranchModel, CompanyBranchModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName, m => m.City,
                (q, e) => q.Where(m => m.City.id == e.ID.ToString() && m.ItemType == CompanyBranchContainerItemTypes.Branch))
            .RunAsync(updateAll);

    public static Task ReplicateCompanyBranchAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        WithClientAsync(connectionString, client => cosmos.ReplicateCompanyBranchAsync<TDbContext>(client, databaseId, updateAll));

    public static Task ReplicateCompanyBranchAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        // CompanyBranchModel embeds City (→Region→Country) and Company — load the whole chain in one query.
        cosmos.SetUp<TDbContext, CompanyBranch>(client, databaseId,
                q => q.Include(x => x.City).ThenInclude(x => x.Region).ThenInclude(x => x.Country).Include(x => x.Company))
            .Replicate<CompanyBranchModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName)
            .RunAsync(updateAll);

    public static Task ReplicateCompanyAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        WithClientAsync(connectionString, client => cosmos.ReplicateCompanyAsync<TDbContext>(client, databaseId, updateAll));

    public static Task ReplicateCompanyAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        cosmos.SetUp<TDbContext, Company>(client, databaseId)
            .Replicate<CompanyModel>(IdentityDatabaseAndContainerNames.CompanyContainerName)
            .UpdatePropertyReference<CompanyModel, CompanyBranchModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName, m => m.Company,
                (q, e) => q.Where(m => m.Company.id == e.ID.ToString()))
            .RunAsync(updateAll);

    public static Task ReplicateTeamAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        WithClientAsync(connectionString, client => cosmos.ReplicateTeamAsync<TDbContext>(client, databaseId, updateAll));

    public static Task ReplicateTeamAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        // TeamModel embeds its branches (TeamCompanyBranches → CompanyBranch) — load them so each carries a real Name —
        // and lists its members (TeamUsers → TeamModel.UserIDs). Two collections, so the query is split: one query
        // would return every branch once for every member.
        cosmos.SetUp<TDbContext, Team>(client, databaseId, q => q
                .Include(x => x.TeamCompanyBranches).ThenInclude(x => x.CompanyBranch)
                .Include(x => x.TeamUsers)
                .AsSplitQuery())
            .Replicate<TeamModel>(IdentityDatabaseAndContainerNames.TeamContainerName)
            .RunAsync(updateAll);

    public static Task ReplicateUserAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        WithClientAsync(connectionString, client => cosmos.ReplicateUserAsync<TDbContext>(client, databaseId, updateAll));

    public static Task ReplicateUserAsync<TDbContext>(this CosmosDBReplication cosmos, CosmosClient client, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        // UserModel lists the assigned named trees (AccessTrees → UserModel.AccessTreeIDs). Without this include the
        // document would get an empty list.
        cosmos.SetUp<TDbContext, User>(client, databaseId, q => q.Include(x => x.AccessTrees))
            .Replicate<UserModel>(IdentityDatabaseAndContainerNames.UserContainerName)
            .RunAsync(updateAll);

    // The connection-string forms open one client for the call and close it when the call ends.
    private static async Task WithClientAsync(string connectionString, Func<CosmosClient, Task> replicate)
    {
        using var client = new CosmosClient(connectionString);
        await replicate(client);
    }
}
