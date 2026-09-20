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
// from the attribute (built-in repository + the automatic ShiftMapper maps), gated by ShiftIdentityActions.Cities. The
// protected-row guard (IsProtected) is enforced by the built-in repository and feature locking by
// FeatureLockSaveValidator. The old CityRepository's jobs are split by responsibility: the Region→Country include
// via IConfiguresShiftRepository (below), the flattened list columns in Mappers/ShiftIdentityMapper.cs (the one
// mapper class — what a member maps from is not the repository's business), and the CountryID-from-Region
// derivation (genuine write logic) via IUpsertsShiftRepository.
[TemporalShiftEntity]
[Table("Cities", Schema = "ShiftIdentity")]
[ShiftEntitySecureEndpoint<CityListDTO, CityDTO, ShiftIdentityActions>("api/IdentityCity", nameof(ShiftIdentityActions.Cities))]
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

    // Shapes the built-in repository: load Region→Country on FindAsync, so the view DTO's Region
    // ShiftEntitySelectDTO gets its Text = Region.Name. The four flattened LIST columns (Region/Country names +
    // display orders) reach through navigations and aren't convention-mappable; they are written in
    // Mappers/ShiftIdentityMapper.cs, which replaces the automatic list map. Everything else on the triple is automatic.
    public void ConfigureRepository(ShiftRepositoryConfigurationContext<City, CityListDTO, CityDTO> context)
    {
        context.Options.IncludeRelatedEntitiesWithFindAsync(i => i.Include(x => x.Region).ThenInclude(x => x.Country));
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