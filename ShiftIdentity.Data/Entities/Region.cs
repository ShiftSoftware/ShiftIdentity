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
// come from the attribute (built-in repository + source-generated mapper), gated by ShiftIdentityActions.Regions.
// The protected-row guard (IsProtected) is enforced by the built-in repository and feature locking by
// FeatureLockSaveValidator. The only repository shaping Region needs — the Country include and the flattened
// list columns the old AutoMapper profile projected — moves onto the entity via IConfiguresShiftRepository.
[TemporalShiftEntity]
[Table("Regions", Schema = "ShiftIdentity")]
[ShiftEntitySecureEndpoint<RegionListDTO, RegionDTO, ShiftIdentityActions>("api/IdentityRegion", nameof(ShiftIdentityActions.Regions), UseGeneratedMapper = true)]
public class Region : ShiftEntity<Region>, IEntityHasCountry<Region>, IEntityHasRegion<Region>, IShiftEntityReplication, IShiftEntityProtectable,
    IConfiguresShiftRepository<Region, RegionListDTO, RegionDTO>
{
    // Moves the old RegionRepository's constructor config onto the entity: load Country on FindAsync (so the
    // view DTO's Country ShiftEntitySelectDTO gets its Text = Country.Name — the generated FK convention fills
    // that when the navigation is loaded), and project the two flattened list columns the AutoMapper profile
    // used to compute. Country (name) and CountryDisplayOrder are NOT convention-mappable (they reach through the
    // Country navigation), so they're supplied as ForList projections spliced into the list SQL.
    public void ConfigureRepository(ShiftRepositoryConfigurationContext<Region, RegionListDTO, RegionDTO> context)
    {
        context.Options.IncludeRelatedEntitiesWithFindAsync(i => i.Include(x => x.Country));

        context.Options.UseGeneratedMapper(map => map
            .ForList(d => d.Country, e => e.Country != null ? e.Country.Name : null)
            .ForList(d => d.CountryDisplayOrder, e => e.Country != null ? e.Country.DisplayOrder : null));
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
