using Microsoft.Azure.Cosmos;
using ShiftSoftware.ShiftEntity.CosmosDbReplication;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Replication;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Replication;

/// <summary>
/// Reusable Cosmos-replication wiring for the ShiftIdentity domain. Each <c>SetUpXReplication</c> configures one
/// entity's replication (its <c>Replicate</c>, or the branch's <c>ReplicateTree</c>); <see cref="SetUpAllIdentityReplications{TDbContext}"/>
/// wires them all, and <see cref="SetUpOrganizationReplications{TDbContext}"/> all but users and authorization. No call passes a mapping delegate: every document is mapped through the host's registered
/// ShiftMapper mapper — the ready-made <see cref="ShiftIdentityReplicationMapper"/>, which
/// <c>AddShiftIdentityDashboard&lt;TDbContext&gt;()</c> registers, so a host that hosts identity has nothing extra to
/// wire. (Registration cannot happen here: this options callback runs per scope over a container that is already
/// built.) A host wiring replication without the dashboard calls <c>AddShiftIdentityReplicationMapper()</c> itself, or
/// includes <see cref="ShiftIdentityReplicationMapper"/> in a mapper of its own; one that does neither fails the first sync loudly
/// (the pipeline checks the pair before writing anything), not silently. The host calls, inside
/// <c>AddShiftEntityCosmosDbReplicationTrigger&lt;DB&gt;(x =&gt; …)</c>:
/// <code>x.SetUpAllIdentityReplications&lt;DB&gt;(client, databaseId);</code>
/// Container names/partition keys are byte-identical to the previous inline configuration.
/// </summary>
public static class IdentityReplicationExtensions
{
    public static void SetUpAllIdentityReplications<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext
    {
        x.SetUpOrganizationReplications<TDbContext>(client, databaseId);
        x.SetUpUserReplication<TDbContext>(client, databaseId);
        x.SetUpAuthorizationReplication<TDbContext>(client, databaseId);
    }

    /// <summary>
    /// Every ShiftIdentity replication but users and authorization: services, departments, brands, regions,
    /// countries, cities, branches, companies and teams. For a host that does not replicate users.
    /// </summary>
    public static void SetUpOrganizationReplications<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext
    {
        x.SetUpServiceReplication<TDbContext>(client, databaseId);
        x.SetUpDepartmentReplication<TDbContext>(client, databaseId);
        x.SetUpBrandReplication<TDbContext>(client, databaseId);
        x.SetUpRegionReplication<TDbContext>(client, databaseId);
        x.SetUpCountryReplication<TDbContext>(client, databaseId);
        x.SetUpCityReplication<TDbContext>(client, databaseId);
        x.SetUpCompanyBranchReplication<TDbContext>(client, databaseId);
        x.SetUpCompanyReplication<TDbContext>(client, databaseId);
        x.SetUpTeamReplication<TDbContext>(client, databaseId);
    }

    /// <summary>
    /// Replicates named grants and assignments alongside the existing Users collection.
    /// Provision all three containers with /id as their partition key before enabling this wiring.
    /// </summary>
    public static void SetUpAuthorizationReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext
    {
        x.SetUpReplication<TDbContext, AccessTree>(client, databaseId)
            .Replicate<AccessTreeModel>(IdentityDatabaseAndContainerNames.AccessTreeContainerName, m => m.id);
        x.SetUpReplication<TDbContext, UserAccessTree>(client, databaseId)
            .Replicate<UserAccessTreeModel>(IdentityDatabaseAndContainerNames.UserAccessTreeContainerName, m => m.id);
        x.SetUpReplication<TDbContext, TeamUser>(client, databaseId)
            .Replicate<TeamUserModel>(IdentityDatabaseAndContainerNames.TeamUserContainerName, m => m.id);
    }

    public static void SetUpServiceReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, Service>(client, databaseId, null)
            .Replicate<ServiceModel>(IdentityDatabaseAndContainerNames.ServiceContainerName, m => m.id);

    public static void SetUpDepartmentReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, Department>(client, databaseId, null)
            .Replicate<DepartmentModel>(IdentityDatabaseAndContainerNames.DepartmentContainerName, m => m.id);

    public static void SetUpBrandReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, Brand>(client, databaseId, null)
            .Replicate<BrandModel>(IdentityDatabaseAndContainerNames.BrandContainerName, m => m.id);

    public static void SetUpRegionReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, Region>(client, databaseId)
            .Replicate<RegionModel>(IdentityDatabaseAndContainerNames.CountryContainerName, m => m.CountryID, m => m.RegionID, m => m.ItemType);

    public static void SetUpCountryReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, Country>(client, databaseId)
            .Replicate<CountryModel>(IdentityDatabaseAndContainerNames.CountryContainerName, m => m.CountryID, m => m.RegionID, m => m.ItemType);

    public static void SetUpCityReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, City>(client, databaseId)
            .Replicate<CityModel>(IdentityDatabaseAndContainerNames.CountryContainerName, m => m.CountryID, m => m.RegionID, m => m.ItemType);

    /// <summary>
    /// The branch's <see cref="BranchModel"/> in <c>Branches</c>, rebuilt from the branch's replication tree after any
    /// save that changes the branch, one of its links, or its city, region, country, company, services, departments or
    /// brands. A sync that fails leaves the branch to the catch-up: the save left it marked as not replicated. Nothing
    /// is written to <c>CompanyBranches</c> any more. Provision the <c>Branches</c> container with <c>/id</c> as its
    /// partition key before enabling this wiring.
    /// </summary>
    public static void SetUpCompanyBranchReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, CompanyBranch>(client, databaseId)
            .ReplicateTree<BranchModel>(IdentityDatabaseAndContainerNames.BranchContainerName, m => m.id);

    public static void SetUpCompanyReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, Company>(client, databaseId)
            .Replicate<CompanyModel>(IdentityDatabaseAndContainerNames.CompanyContainerName, m => m.id);

    public static void SetUpTeamReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, Team>(client, databaseId)
            .Replicate<TeamModel>(IdentityDatabaseAndContainerNames.TeamContainerName, m => m.id);

    public static void SetUpUserReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, ShiftSoftware.ShiftIdentity.Data.Entities.User>(client, databaseId)
            .Replicate<UserModel>(IdentityDatabaseAndContainerNames.UserContainerName, m => m.id);
}
