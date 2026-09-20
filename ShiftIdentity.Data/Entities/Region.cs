using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model.Flags;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

// Attribute-driven endpoint (Rung B): Region has no controller and no repository class. The secure CRUD routes
// come from the attribute (built-in repository + the automatic ShiftMapper maps), gated by ShiftIdentityActions.Regions.
// The protected-row guard (IsProtected) is enforced by the built-in repository and feature locking by
// FeatureLockSaveValidator. The only repository shaping Region needs — the Country include — is on the entity via
// IConfiguresShiftRepository; the flattened list columns are in the mapper class (Mappers/ShiftIdentityMapper.cs).
[TemporalShiftEntity]
[Table("Regions", Schema = "ShiftIdentity")]
[ShiftEntitySecureEndpoint<RegionListDTO, RegionDTO, ShiftIdentityActions>("api/IdentityRegion", nameof(ShiftIdentityActions.Regions))]
public class Region : ShiftEntity<Region>, IEntityHasCountry<Region>, IEntityHasRegion<Region>, IShiftEntityReplication, IShiftEntityProtectable,
    IConfiguresShiftRepository<Region, RegionListDTO, RegionDTO>
{
    // Shapes the built-in repository: load Country on FindAsync, so the view DTO's Country ShiftEntitySelectDTO
    // gets its Text = Country.Name (the select convention fills that when the navigation is loaded). The two
    // flattened LIST columns — Country (name) and CountryDisplayOrder reach through the Country navigation, so
    // they are not convention-mappable — are written in Mappers/ShiftIdentityMapper.cs, which replaces the
    // automatic list map.
    public void ConfigureRepository(ShiftRepositoryConfigurationContext<Region, RegionListDTO, RegionDTO> context)
    {
        context.Options.IncludeRelatedEntitiesWithFindAsync(i => i.Include(x => x.Country));
    }

    /// <inheritdoc />
    public DateTimeOffset? LastReplicationDate { get; set; }

    /// <inheritdoc />
    public string? LastReplicationStamp { get; set; }

    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
    public string? ShortCode { get; set; }
    public bool IsProtected { get; set; }
    public long? CountryID { get; set; }
    public virtual Country? Country { get; set; }
    public long? RegionID { get; set; }
    public string? Flag { get; set; }
    public int? DisplayOrder { get; set; }

    public virtual ICollection<CompanyBranch> CompanyBranches { get; set; }

    public Region()
    {
        CompanyBranches = new HashSet<CompanyBranch>();
    }

}
