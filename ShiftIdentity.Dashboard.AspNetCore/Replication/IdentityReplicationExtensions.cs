using Microsoft.Azure.Cosmos;
using ShiftSoftware.ShiftEntity.CosmosDbReplication;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Replication;
using System.Linq;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Replication;

/// <summary>
/// Reusable Cosmos-replication wiring for the ShiftIdentity domain. Each <c>SetUpXReplication</c> configures one
/// entity's replication (its <c>Replicate</c> + any reference updates); <see cref="SetUpAllIdentityReplications{TDbContext}"/>
/// wires them all. Every mapping is supplied as an explicit manual delegate (the <c>ToXModel()</c> methods in
/// <c>ShiftIdentity.Data</c>), so replication no longer depends on AutoMapper. The host calls, inside
/// <c>AddShiftEntityCosmosDbReplicationTrigger&lt;DB&gt;(x =&gt; …)</c>:
/// <code>x.SetUpAllIdentityReplications&lt;DB&gt;(client, databaseId);</code>
/// Container names/partition keys are byte-identical to the previous inline configuration.
/// </summary>
public static class IdentityReplicationExtensions
{
    public static void SetUpAllIdentityReplications<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext
    {
        x.SetUpServiceReplication<TDbContext>(client, databaseId);
        x.SetUpCompanyBranchServiceReplication<TDbContext>(client, databaseId);
        x.SetUpCompanyBranchDepartmentReplication<TDbContext>(client, databaseId);
        x.SetUpDepartmentReplication<TDbContext>(client, databaseId);
        x.SetUpBrandReplication<TDbContext>(client, databaseId);
        x.SetUpCompanyBranchBrandReplication<TDbContext>(client, databaseId);
        x.SetUpRegionReplication<TDbContext>(client, databaseId);
        x.SetUpCountryReplication<TDbContext>(client, databaseId);
        x.SetUpCityReplication<TDbContext>(client, databaseId);
        x.SetUpCompanyBranchReplication<TDbContext>(client, databaseId);
        x.SetUpCompanyReplication<TDbContext>(client, databaseId);
        x.SetUpTeamReplication<TDbContext>(client, databaseId);
        x.SetUpUserReplication<TDbContext>(client, databaseId);
    }

    public static void SetUpServiceReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, Service>(client, databaseId, null)
            .Replicate<ServiceModel>(IdentityDatabaseAndContainerNames.ServiceContainerName, m => m.id,
                e => e.Entity.ToServiceModel())
            .UpdateReference<CompanyBranchSubItemModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName,
                (q, e) => q.Where(m => m.ItemType == CompanyBranchContainerItemTypes.Service && m.id == e.Entity.ID.ToString()),
                (e, existing) => e.Entity.ApplyToCompanyBranchSubItem(existing));

    public static void SetUpCompanyBranchServiceReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, CompanyBranchService>(client, databaseId, null)
            .Replicate<CompanyBranchSubItemModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName, m => m.BranchID, m => m.ItemType,
                e => e.Entity.ToCompanyBranchSubItemModel());

    public static void SetUpCompanyBranchDepartmentReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, CompanyBranchDepartment>(client, databaseId, null)
            .Replicate<CompanyBranchSubItemModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName, m => m.BranchID, m => m.ItemType,
                e => e.Entity.ToCompanyBranchSubItemModel());

    public static void SetUpDepartmentReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, Department>(client, databaseId, null)
            .Replicate<DepartmentModel>(IdentityDatabaseAndContainerNames.DepartmentContainerName, m => m.id,
                e => e.Entity.ToDepartmentModel())
            .UpdateReference<CompanyBranchSubItemModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName,
                (q, e) => q.Where(m => m.ItemType == CompanyBranchContainerItemTypes.Department && m.id == e.Entity.ID.ToString()),
                (e, existing) => e.Entity.ApplyToCompanyBranchSubItem(existing));

    public static void SetUpBrandReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, Brand>(client, databaseId, null)
            .Replicate<BrandModel>(IdentityDatabaseAndContainerNames.BrandContainerName, m => m.id,
                e => e.Entity.ToBrandModel())
            .UpdateReference<CompanyBranchSubItemModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName,
                (q, e) => q.Where(m => m.id == e.Entity.ID.ToString() && m.ItemType == CompanyBranchContainerItemTypes.Brand),
                (e, existing) => e.Entity.ApplyToCompanyBranchSubItem(existing));

    public static void SetUpCompanyBranchBrandReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, CompanyBranchBrand>(client, databaseId)
            .Replicate<CompanyBranchSubItemModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName, m => m.BranchID, m => m.ItemType,
                e => e.Entity.ToCompanyBranchSubItemModel());

    public static void SetUpRegionReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, Region>(client, databaseId)
            .Replicate<RegionModel>(IdentityDatabaseAndContainerNames.CountryContainerName, m => m.CountryID, m => m.RegionID, m => m.ItemType,
                e => e.Entity.ToRegionModel())
            .UpdatePropertyReference<CityRegionModel, CompanyBranchModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName, m => m.City.Region,
                (q, e) => q.Where(m => m.City.Region.id == e.Entity.ID.ToString() && m.ItemType == CompanyBranchContainerItemTypes.Branch),
                e => e.Entity.ToCityRegionModel());

    public static void SetUpCountryReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, Country>(client, databaseId)
            .Replicate<CountryModel>(IdentityDatabaseAndContainerNames.CountryContainerName, m => m.CountryID, m => m.RegionID, m => m.ItemType,
                e => e.Entity.ToCountryModel())
            .UpdatePropertyReference<CountryModel, CompanyBranchModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName, m => m.City.Region.Country,
                (q, e) => q.Where(m => m.City.Region.Country.id == e.Entity.ID.ToString() && m.ItemType == CompanyBranchContainerItemTypes.Branch),
                e => e.Entity.ToCountryModel());

    public static void SetUpCityReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, City>(client, databaseId)
            .Replicate<CityModel>(IdentityDatabaseAndContainerNames.CountryContainerName, m => m.CountryID, m => m.RegionID, m => m.ItemType,
                e => e.Entity.ToCityModel())
            .UpdatePropertyReference<CityCompanyBranchModel, CompanyBranchModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName, m => m.City,
                (q, e) => q.Where(m => m.City.id == e.Entity.ID.ToString() && m.ItemType == CompanyBranchContainerItemTypes.Branch),
                e => e.Entity.ToCityCompanyBranchModel());

    public static void SetUpCompanyBranchReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, CompanyBranch>(client, databaseId)
            .Replicate<CompanyBranchModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName, m => m.BranchID, m => m.ItemType,
                e => e.Entity.ToCompanyBranchModel());

    public static void SetUpCompanyReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, Company>(client, databaseId)
            .Replicate<CompanyModel>(IdentityDatabaseAndContainerNames.CompanyContainerName, m => m.id,
                e => e.Entity.ToCompanyModel())
            .UpdatePropertyReference<CompanyModel, CompanyBranchModel>(IdentityDatabaseAndContainerNames.CompanyBranchContainerName, m => m.Company,
                (q, e) => q.Where(m => m.Company.id == e.Entity.ID.ToString()),
                e => e.Entity.ToCompanyModel());

    public static void SetUpTeamReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, Team>(client, databaseId)
            .Replicate<TeamModel>(IdentityDatabaseAndContainerNames.TeamContainerName, m => m.id,
                e => e.Entity.ToTeamModel());

    public static void SetUpUserReplication<TDbContext>(this ShiftEntityCosmosDbOptions x, CosmosClient client, string databaseId)
        where TDbContext : ShiftIdentityDbContext =>
        x.SetUpReplication<TDbContext, ShiftSoftware.ShiftIdentity.Data.Entities.User>(client, databaseId)
            .Replicate<UserModel>(IdentityDatabaseAndContainerNames.UserContainerName, m => m.id,
                e => e.Entity.ToUserModel());
}
