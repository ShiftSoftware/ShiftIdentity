using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using System.Collections.Generic;
using System.Linq;

namespace ShiftSoftware.ShiftIdentity.Data.Replication;

/// <summary>
/// Manual, AutoMapper-free <c>Entity → *Model</c> mappings for Cosmos replication. Each method reproduces the
/// corresponding AutoMapper profile in <c>AutoMapperProfiles/*.cs</c> EXACTLY — the explicit <c>ForMember</c>
/// overrides, the members AutoMapper filled by convention, AND the <see cref="ReplicationModel"/> base/audit fields —
/// so replicated Cosmos documents are byte-identical. The AutoMapper profiles are intentionally KEPT (backward
/// compatibility); these methods are what the <c>SetUp*Replication</c> extensions pass as the manual mapping delegate,
/// removing AutoMapper from the replication path.
/// </summary>
public static class IdentityReplicationMappingExtensions
{
    // The ReplicationModel base audit fields every profile mapped by convention. `id` is NOT set here — each map sets
    // it explicitly (usually ID.ToString(), sometimes a foreign key), matching the profile's ForMember(dest.id, …).
    private static TModel WithAudit<TEntity, TModel>(this TModel model, TEntity entity)
        where TEntity : ShiftEntity<TEntity>
        where TModel : ReplicationModel
    {
        model.IsDeleted = entity.IsDeleted;
        model.CreateDate = entity.CreateDate;
        model.LastSaveDate = entity.LastSaveDate;
        model.CreatedByUserID = entity.CreatedByUserID?.ToString();
        model.LastSavedByUserID = entity.LastSavedByUserID?.ToString();
        return model;
    }

    // ─────────────────────────────── Brand ───────────────────────────────
    public static BrandModel ToBrandModel(this Brand src) => new BrandModel
    {
        id = src.ID.ToString(),
        Name = src.Name,
        IntegrationId = src.IntegrationId,
        BrandID = src.BrandID,
    }.WithAudit(src);

    // Brand → CompanyBranchSubItemModel is used only to UPDATE existing branch sub-items (UpdateReference), so it maps
    // ONTO the existing document, leaving BranchID (Brand has no source for it) untouched — matching AutoMapper's Map(src, dest).
    public static CompanyBranchSubItemModel ApplyToCompanyBranchSubItem(this Brand src, CompanyBranchSubItemModel dest)
    {
        dest.id = src.ID.ToString();
        dest.Name = src.Name;
        dest.IntegrationId = src.IntegrationId;
        dest.ItemType = CompanyBranchContainerItemTypes.Brand;
        return dest.WithAudit(src);
    }

    // ─────────────────────────────── Service ───────────────────────────────
    public static ServiceModel ToServiceModel(this Service src) => new ServiceModel
    {
        id = src.ID.ToString(),
        Name = src.Name,
        IntegrationId = src.IntegrationId,
    }.WithAudit(src);

    public static CompanyBranchSubItemModel ApplyToCompanyBranchSubItem(this Service src, CompanyBranchSubItemModel dest)
    {
        dest.id = src.ID.ToString();
        dest.Name = src.Name;
        dest.IntegrationId = src.IntegrationId;
        dest.ItemType = CompanyBranchContainerItemTypes.Service;
        return dest.WithAudit(src);
    }

    // ─────────────────────────────── Department ───────────────────────────────
    public static DepartmentModel ToDepartmentModel(this Department src) => new DepartmentModel
    {
        id = src.ID.ToString(),
        Name = src.Name,
        IntegrationId = src.IntegrationId,
    }.WithAudit(src);

    public static CompanyBranchSubItemModel ApplyToCompanyBranchSubItem(this Department src, CompanyBranchSubItemModel dest)
    {
        dest.id = src.ID.ToString();
        dest.Name = src.Name;
        dest.IntegrationId = src.IntegrationId;
        dest.ItemType = CompanyBranchContainerItemTypes.Department;
        return dest.WithAudit(src);
    }

    // ───────── Branch sub-items produced FRESH from the M:N join entities (General.cs profile) ─────────
    // NOTE: the Service/Department/Brand navigation is NULL at replication time — the M:N join is inserted with only
    // its FK set (see CompanyBranch's ConfigureRepository). AutoMapper null-propagates src.Service.Name to null, so
    // these use ?. to match EXACTLY (a plain src.Service.Name would NRE and silently kill the sub-item replication).
    public static CompanyBranchSubItemModel ToCompanyBranchSubItemModel(this CompanyBranchService src) => new CompanyBranchSubItemModel
    {
        id = src.ServiceID.ToString(),
        Name = src.Service?.Name!,
        IntegrationId = src.Service?.IntegrationId,
        BranchID = src.CompanyBranchID.ToString(),
        ItemType = CompanyBranchContainerItemTypes.Service,
    }.WithAudit(src);

    public static CompanyBranchSubItemModel ToCompanyBranchSubItemModel(this CompanyBranchDepartment src) => new CompanyBranchSubItemModel
    {
        id = src.DepartmentID.ToString(),
        Name = src.Department?.Name!,
        IntegrationId = src.Department?.IntegrationId,
        BranchID = src.CompanyBranchID.ToString(),
        ItemType = CompanyBranchContainerItemTypes.Department,
    }.WithAudit(src);

    public static CompanyBranchSubItemModel ToCompanyBranchSubItemModel(this CompanyBranchBrand src) => new CompanyBranchSubItemModel
    {
        id = src.BrandID.ToString(),
        Name = src.Brand?.Name!,
        IntegrationId = src.Brand?.IntegrationId,
        BranchID = src.CompanyBranchID.ToString(),
        ItemType = CompanyBranchContainerItemTypes.Brand,
    }.WithAudit(src);

    // Team → TeamModel embeds branches via TeamCompanyBranch. Note: `id` is the JOIN row's own ID (not the branch ID).
    public static CompanyBranchSubItemModel ToCompanyBranchSubItemModel(this TeamCompanyBranch src) => new CompanyBranchSubItemModel
    {
        id = src.ID.ToString(),
        Name = src.CompanyBranch?.Name!,
        IntegrationId = src.CompanyBranch?.IntegrationId,
        // AutoMapper substitutes default(long)=0 for the value-type ID when the nav is null → "0" (not null).
        BranchID = (src.CompanyBranch?.ID ?? 0).ToString(),
        ItemType = CompanyBranchContainerItemTypes.Branch,
    }.WithAudit(src);

    // ─────────────────────────────── Country ───────────────────────────────
    // Country.cs has NO explicit id map (AutoMapper filled it by case-insensitive convention from ID); set it manually.
    public static CountryModel ToCountryModel(this Country src) => new CountryModel
    {
        id = src.ID.ToString(),
        CountryID = src.ID,          // override: MapFrom(src.ID) — the entity's own CountryID field is NOT used
        RegionID = null,             // no source on Country → AutoMapper leaves default
        ItemType = CountryContainerItemTypes.Country,
        Name = src.Name,
        IntegrationId = src.IntegrationId,
        ShortCode = src.ShortCode,
        CallingCode = src.CallingCode,
        IsProtected = src.IsProtected,
        Flag = src.Flag,
        DisplayOrder = src.DisplayOrder,
    }.WithAudit(src);

    // ─────────────────────────────── Region ───────────────────────────────
    public static RegionModel ToRegionModel(this Region src) => new RegionModel
    {
        id = src.ID.ToString(),
        CountryID = src.CountryID,   // override
        RegionID = src.ID,           // override: MapFrom(src.ID.ToString()) round-trips to the entity ID (long?)
        Name = src.Name,
        IntegrationId = src.IntegrationId,
        ShortCode = src.ShortCode,
        IsProtected = src.IsProtected,
        ItemType = CountryContainerItemTypes.Region,
        Flag = src.Flag,
        DisplayOrder = src.DisplayOrder,
    }.WithAudit(src);

    // Region → CityRegionModel (nested under CompanyBranchModel.City.Region). No ItemType; ids are STRING here.
    public static CityRegionModel ToCityRegionModel(this Region src) => new CityRegionModel
    {
        id = src.ID.ToString(),
        CountryID = src.CountryID?.ToString(),   // long? → string
        RegionID = src.ID.ToString(),
        Name = src.Name,
        IntegrationId = src.IntegrationId,
        ShortCode = src.ShortCode,
        IsProtected = src.IsProtected,
        Flag = src.Flag,
        DisplayOrder = src.DisplayOrder,
        Country = src.Country?.ToCountryModel(),
    }.WithAudit(src);

    // ─────────────────────────────── City ───────────────────────────────
    public static CityModel ToCityModel(this City src) => new CityModel
    {
        id = src.ID.ToString(),
        Name = src.Name,
        IntegrationId = src.IntegrationId,
        CountryID = src.CountryID,
        RegionID = src.RegionID,
        IsProtected = src.IsProtected,
        ItemType = CountryContainerItemTypes.City,
        CityID = src.CityID,
        DisplayOrder = src.DisplayOrder,
    }.WithAudit(src);

    // City → CityCompanyBranchModel (nested under CompanyBranchModel.City). No ItemType/CountryID/RegionID/CityID.
    public static CityCompanyBranchModel ToCityCompanyBranchModel(this City src) => new CityCompanyBranchModel
    {
        id = src.ID.ToString(),
        Name = src.Name,
        IntegrationId = src.IntegrationId,
        IsProtected = src.IsProtected,
        DisplayOrder = src.DisplayOrder,
        Region = src.Region?.ToCityRegionModel(),
    }.WithAudit(src);

    // ─────────────────────────────── Company ───────────────────────────────
    public static CompanyModel ToCompanyModel(this Company src) => new CompanyModel
    {
        id = src.ID.ToString(),
        Name = src.Name,
        LegalName = src.LegalName,
        IntegrationId = src.IntegrationId,
        ShortCode = src.ShortCode,
        CompanyType = src.CompanyType,
        Logo = src.Logo,
        HQPhone = src.HQPhone,
        HQEmail = src.HQEmail,
        HQAddress = src.HQAddress,
        Website = src.Website,
        IsProtected = src.IsProtected,
        TerminationDate = src.TerminationDate,
        CustomFields = src.CustomFields,
        ParentCompanyID = src.ParentCompanyID,
        CompanyID = src.CompanyID,
        DisplayOrder = src.DisplayOrder,
    }.WithAudit(src);

    // ─────────────────────────────── CompanyBranch ───────────────────────────────
    public static CompanyBranchModel ToCompanyBranchModel(this CompanyBranch src) => new CompanyBranchModel
    {
        id = src.ID.ToString(),
        Name = src.Name,
        Phone = src.Phone,
        Phones = src.Phones,
        ShortPhone = src.ShortPhone,
        Email = src.Email,
        Emails = src.Emails,
        Address = src.Address,
        IntegrationId = src.IntegrationId,
        ShortCode = src.ShortCode,
        TerminationDate = src.TerminationDate,
        Location = (string.IsNullOrWhiteSpace(src.Longitude) || string.IsNullOrWhiteSpace(src.Latitude))
            ? null
            : new Location(new decimal[] { decimal.Parse(src.Longitude), decimal.Parse(src.Latitude) }),
        Photos = src.Photos,
        MobilePhotos = src.MobilePhotos,
        WorkingHours = src.WorkingHours,
        WorkingDays = src.WorkingDays,
        IsProtected = src.IsProtected,
        City = src.City?.ToCityCompanyBranchModel(),
        Company = src.Company?.ToCompanyModel(),
        BranchID = src.ID.ToString(),
        ItemType = CompanyBranchContainerItemTypes.Branch,
        CustomFields = src.CustomFields,
        RegionID = src.RegionID,
        CityID = src.CityID,
        CompanyID = src.CompanyID,
        CountryID = src.CountryID,
        CompanyBranchID = src.CompanyBranchID,
        DisplayOrder = src.DisplayOrder,
        DisplayName = src.DisplayName,
        Description = src.Description,
        WebsiteURL = null,   // no source on CompanyBranch → AutoMapper leaves default
        PublishTargets = src.PublishTargets,
    }.WithAudit(src);

    // ─────────────────────────────── Team ───────────────────────────────
    public static TeamModel ToTeamModel(this Team src) => new TeamModel
    {
        id = src.ID.ToString(),
        Name = src.Name,
        IntegrationId = src.IntegrationId,
        Tags = src.Tags,
        CompanyID = src.CompanyID,
        TeamID = src.TeamID,
        CompanyBranches = (src.TeamCompanyBranches ?? new List<TeamCompanyBranch>())
            .Select(x => x.ToCompanyBranchSubItemModel())
            .ToList(),
    }.WithAudit(src);

    // ─────────────────────────────── User ───────────────────────────────
    public static UserModel ToUserModel(this User src) => new UserModel
    {
        id = src.ID.ToString(),
        FullName = src.FullName,
        Username = src.Username,
        Phone = src.Phone,
        Email = src.Email,
        IntegrationId = src.IntegrationId,
        IsProtected = src.IsProtected,
        CompanyID = src.CompanyID,
        CompanyBranchID = src.CompanyBranchID,
        RegionID = src.RegionID,
        CountryID = src.CountryID,
    }.WithAudit(src);
}
