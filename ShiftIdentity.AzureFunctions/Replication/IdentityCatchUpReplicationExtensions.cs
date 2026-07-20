using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.CosmosDbReplication.Services;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Replication;
using System.Linq;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.AzureFunctions.Replication;

/// <summary>
/// Reusable Cosmos "catch-up" replication for the ShiftIdentity domain — the function/timer counterpart to the
/// trigger-side <c>SetUpAllIdentityReplications</c>. Each <c>ReplicateXAsync</c> re-syncs ONE entity's rows to Cosmos
/// through the framework's <see cref="CosmosDBReplication"/> service; <see cref="ReplicateAllAsync{TDbContext}"/> runs
/// them all. By default only DIRTY rows are synced (<c>updateAll: false</c> — the incremental hourly catch-up); pass
/// <c>updateAll: true</c> for a full backfill (the on-demand HTTP endpoint).
///
/// Every mapping is the explicit manual <c>ToXModel()</c> delegate from <c>ShiftIdentity.Data</c> — the same delegates
/// the trigger side uses — so replication does NOT depend on AutoMapper and the Cosmos documents are byte-identical.
///
/// The catch-up side has no <c>IShiftEntityPrepareForReplicationAsync</c> hook (that runs only inside the save
/// trigger), so navigations the models depend on (City→Region→Country, CompanyBranch's City/Company, the M:N joins'
/// Service/Department/Brand, Team's branches) are loaded HERE via the <c>SetUp(…, query)</c> include shaper.
/// Containers/partition data are byte-identical to the trigger configuration.
/// </summary>
public static class IdentityCatchUpReplicationExtensions
{
    /// <summary>Runs every ShiftIdentity catch-up replication in dependency order.</summary>
    public static async Task ReplicateAllAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext
    {
        await cosmos.ReplicateServiceAsync<TDbContext>(connectionString, databaseId, updateAll);
        await cosmos.ReplicateCompanyBranchServiceAsync<TDbContext>(connectionString, databaseId, updateAll);
        await cosmos.ReplicateCompanyBranchDepartmentAsync<TDbContext>(connectionString, databaseId, updateAll);
        await cosmos.ReplicateDepartmentAsync<TDbContext>(connectionString, databaseId, updateAll);
        await cosmos.ReplicateBrandAsync<TDbContext>(connectionString, databaseId, updateAll);
        await cosmos.ReplicateCompanyBranchBrandAsync<TDbContext>(connectionString, databaseId, updateAll);
        await cosmos.ReplicateRegionAsync<TDbContext>(connectionString, databaseId, updateAll);
        await cosmos.ReplicateCountryAsync<TDbContext>(connectionString, databaseId, updateAll);
        await cosmos.ReplicateCityAsync<TDbContext>(connectionString, databaseId, updateAll);
        await cosmos.ReplicateCompanyBranchAsync<TDbContext>(connectionString, databaseId, updateAll);
        await cosmos.ReplicateCompanyAsync<TDbContext>(connectionString, databaseId, updateAll);
        await cosmos.ReplicateTeamAsync<TDbContext>(connectionString, databaseId, updateAll);
        await cosmos.ReplicateUserAsync<TDbContext>(connectionString, databaseId, updateAll);
    }

    public static Task ReplicateServiceAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        cosmos.SetUp<TDbContext, Service>(connectionString, databaseId)
            .Replicate<ServiceModel>(IdentityDatabaseAndContainerNames.ServiceContainerName,
                e => e.ToServiceModel())
            .UpdateReference<CompanyBranchSubItemModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName,
                (q, e) => q.Where(m => m.ItemType == CompanyBranchContainerItemTypes.Service && m.id == e.ID.ToString()),
                (e, existing) => e.ApplyToCompanyBranchSubItem(existing))
            .RunAsync(updateAll);

    public static Task ReplicateCompanyBranchServiceAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        // Include Service — the sub-item carries its Name/IntegrationId (null on the bare join otherwise).
        cosmos.SetUp<TDbContext, CompanyBranchService>(connectionString, databaseId, q => q.Include(x => x.Service))
            .Replicate<CompanyBranchSubItemModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName,
                e => e.ToCompanyBranchSubItemModel())
            .RunAsync(updateAll);

    public static Task ReplicateCompanyBranchDepartmentAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        cosmos.SetUp<TDbContext, CompanyBranchDepartment>(connectionString, databaseId, q => q.Include(x => x.Department))
            .Replicate<CompanyBranchSubItemModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName,
                e => e.ToCompanyBranchSubItemModel())
            .RunAsync(updateAll);

    public static Task ReplicateDepartmentAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        cosmos.SetUp<TDbContext, Department>(connectionString, databaseId)
            .Replicate<DepartmentModel>(IdentityDatabaseAndContainerNames.DepartmentContainerName,
                e => e.ToDepartmentModel())
            .UpdateReference<CompanyBranchSubItemModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName,
                (q, e) => q.Where(m => m.ItemType == CompanyBranchContainerItemTypes.Department && m.id == e.ID.ToString()),
                (e, existing) => e.ApplyToCompanyBranchSubItem(existing))
            .RunAsync(updateAll);

    public static Task ReplicateBrandAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        cosmos.SetUp<TDbContext, Brand>(connectionString, databaseId)
            .Replicate<BrandModel>(IdentityDatabaseAndContainerNames.BrandContainerName,
                e => e.ToBrandModel())
            .UpdateReference<CompanyBranchSubItemModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName,
                (q, e) => q.Where(m => m.id == e.ID.ToString() && m.ItemType == CompanyBranchContainerItemTypes.Brand),
                (e, existing) => e.ApplyToCompanyBranchSubItem(existing))
            .RunAsync(updateAll);

    public static Task ReplicateCompanyBranchBrandAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        cosmos.SetUp<TDbContext, CompanyBranchBrand>(connectionString, databaseId, q => q.Include(x => x.Brand))
            .Replicate<CompanyBranchSubItemModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName,
                e => e.ToCompanyBranchSubItemModel())
            .RunAsync(updateAll);

    public static Task ReplicateRegionAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        // Include Country — the nested CityRegionModel.Country reference is built from it.
        cosmos.SetUp<TDbContext, Region>(connectionString, databaseId, q => q.Include(x => x.Country))
            .Replicate<RegionModel>(IdentityDatabaseAndContainerNames.CountryContainerName,
                e => e.ToRegionModel())
            .UpdatePropertyReference<CityRegionModel, CompanyBranchModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName, m => m.City.Region,
                (q, e) => q.Where(m => m.City.Region.id == e.ID.ToString() && m.ItemType == CompanyBranchContainerItemTypes.Branch),
                e => e.ToCityRegionModel())
            .RunAsync(updateAll);

    public static Task ReplicateCountryAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        cosmos.SetUp<TDbContext, Country>(connectionString, databaseId)
            .Replicate<CountryModel>(IdentityDatabaseAndContainerNames.CountryContainerName,
                e => e.ToCountryModel())
            .UpdatePropertyReference<CountryModel, CompanyBranchModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName, m => m.City.Region.Country,
                (q, e) => q.Where(m => m.City.Region.Country.id == e.ID.ToString() && m.ItemType == CompanyBranchContainerItemTypes.Branch),
                e => e.ToCountryModel())
            .RunAsync(updateAll);

    public static Task ReplicateCityAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        // Include Region→Country — the nested CityCompanyBranchModel.Region (and its Country) are built from them.
        cosmos.SetUp<TDbContext, City>(connectionString, databaseId, q => q.Include(x => x.Region).ThenInclude(x => x.Country))
            .Replicate<CityModel>(IdentityDatabaseAndContainerNames.CountryContainerName,
                e => e.ToCityModel())
            .UpdatePropertyReference<CityCompanyBranchModel, CompanyBranchModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName, m => m.City,
                (q, e) => q.Where(m => m.City.id == e.ID.ToString() && m.ItemType == CompanyBranchContainerItemTypes.Branch),
                e => e.ToCityCompanyBranchModel())
            .RunAsync(updateAll);

    public static Task ReplicateCompanyBranchAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        // CompanyBranchModel embeds City (→Region→Country) and Company — load the whole chain in one query.
        cosmos.SetUp<TDbContext, CompanyBranch>(connectionString, databaseId,
                q => q.Include(x => x.City).ThenInclude(x => x.Region).ThenInclude(x => x.Country).Include(x => x.Company))
            .Replicate<CompanyBranchModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName,
                e => e.ToCompanyBranchModel())
            .RunAsync(updateAll);

    public static Task ReplicateCompanyAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        cosmos.SetUp<TDbContext, Company>(connectionString, databaseId)
            .Replicate<CompanyModel>(IdentityDatabaseAndContainerNames.CompanyContainerName,
                e => e.ToCompanyModel())
            .UpdatePropertyReference<CompanyModel, CompanyBranchModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName, m => m.Company,
                (q, e) => q.Where(m => m.Company.id == e.ID.ToString()),
                e => e.ToCompanyModel())
            .RunAsync(updateAll);

    public static Task ReplicateTeamAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        // TeamModel embeds its branches (TeamCompanyBranches → CompanyBranch) — load them so each carries a real Name.
        cosmos.SetUp<TDbContext, Team>(connectionString, databaseId, q => q.Include(x => x.TeamCompanyBranches).ThenInclude(x => x.CompanyBranch))
            .Replicate<TeamModel>(IdentityDatabaseAndContainerNames.TeamContainerName,
                e => e.ToTeamModel())
            .RunAsync(updateAll);

    public static Task ReplicateUserAsync<TDbContext>(this CosmosDBReplication cosmos, string connectionString, string databaseId, bool updateAll = false)
        where TDbContext : ShiftIdentityDbContext =>
        cosmos.SetUp<TDbContext, User>(connectionString, databaseId)
            .Replicate<UserModel>(IdentityDatabaseAndContainerNames.UserContainerName,
                e => e.ToUserModel())
            .RunAsync(updateAll);
}
