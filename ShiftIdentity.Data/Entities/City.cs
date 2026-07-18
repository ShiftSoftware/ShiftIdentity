using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model.Flags;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.City;
using ShiftSoftware.ShiftIdentity.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations.Schema;

using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;


// Attribute-driven endpoint (Rung B): City has no controller and no repository class. The secure CRUD routes come
// from the attribute (built-in repository + source-generated mapper), gated by ShiftIdentityActions.Cities. The
// protected-row guard (IsProtected) is enforced by the built-in repository and feature locking by
// FeatureLockSaveValidator. The old CityRepository's two jobs move onto the entity: the Region→Country include +
// flattened list columns via IConfiguresShiftRepository, and the CountryID-from-Region derivation (genuine write
// logic) via IUpsertsShiftRepository.
[TemporalShiftEntity]
[Table("Cities", Schema = "ShiftIdentity")]
[ShiftEntitySecureEndpoint<CityListDTO, CityDTO, ShiftIdentityActions>("api/IdentityCity", nameof(ShiftIdentityActions.Cities), UseGeneratedMapper = true)]
public class City : ShiftEntity<City>, IEntityHasCity<City>, IEntityHasRegion<City>, IEntityHasCountry<City>, IShiftEntityReplication, IShiftEntityProtectable,
    IConfiguresShiftRepository<City, CityListDTO, CityDTO>,
    IUpsertsShiftRepository<City, CityListDTO, CityDTO>
{
    /// <inheritdoc />
    public DateTimeOffset? LastReplicationDate { get; set; }

    /// <inheritdoc />
    public string? LastReplicationStamp { get; set; }

    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
    public long? RegionID { get; set; }
    public virtual Region? Region { get; set; } = default!;
    public bool IsProtected { get; set; }
    public virtual ICollection<CompanyBranch> CompanyBranches { get; set; }
    public long? CountryID { get; set; }
    public long? CityID { get; set; }

    public int? DisplayOrder { get; set; }

    public City()
    {
        CompanyBranches = new HashSet<CompanyBranch>();
    }

    // Moves the old CityRepository's constructor config onto the entity: load Region→Country on FindAsync (so the
    // view DTO's Region ShiftEntitySelectDTO gets its Text = Region.Name), and project the four flattened list
    // columns (Region/Country names + display orders) the AutoMapper profile used to compute — these reach through
    // navigations and aren't convention-mappable, so they're supplied as ForList projections spliced into the SQL.
    public void ConfigureRepository(ShiftRepositoryConfigurationContext<City, CityListDTO, CityDTO> context)
    {
        context.Options.IncludeRelatedEntitiesWithFindAsync(i => i.Include(x => x.Region).ThenInclude(x => x.Country));

        context.Options.UseGeneratedMapper(map => map
            .ForList(d => d.Region, e => e.Region != null ? e.Region.Name : null)
            .ForList(d => d.Country, e => e.Region != null && e.Region.Country != null ? e.Region.Country.Name : null)
            .ForList(d => d.CountryDisplayOrder, e => e.Region != null && e.Region.Country != null ? e.Region.Country.DisplayOrder : null)
            .ForList(d => d.RegionDisplayOrder, e => e.Region != null ? e.Region.DisplayOrder : null));
    }

    // Genuine write logic from the old CityRepository.UpsertAsync: CountryID is denormalized from the selected
    // Region. Set it BEFORE context.Base() so the default's country-scoped data-level write check (City is
    // IEntityHasCountry) authorizes against the real country. MapToEntity has no source for CountryID (CityDTO has
    // no Country member), so it won't overwrite this. The lookup ignores data-level access and global filters,
    // matching the old regionRepo.FindAsync(disableDefaultDataLevelAccess: true, disableGlobalFilters: true).
    public async ValueTask<City> UpsertAsync(
        City entity,
        CityDTO dto,
        ActionTypes actionType,
        long? userId,
        Guid? idempotencyKey,
        bool disableDefaultDataLevelAccess,
        bool disableGlobalFilters,
        ShiftRepositoryUpsertContext<City, CityListDTO, CityDTO> context)
    {
        var db = context.Services.GetRequiredService<ShiftIdentityDbContext>();

        var regionId = dto.Region.Value.ToLong();

        entity.CountryID = await db.Regions
            .AsNoTracking()
            .Where(x => x.ID == regionId)
            .Select(x => x.CountryID)
            .FirstOrDefaultAsync();

        return await context.Base();
    }
}