using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Flags;
using ShiftSoftware.ShiftEntity.Model.Replication;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;


[TemporalShiftEntity]
[Table("Cities", Schema = "ShiftIdentity")]
public class City : ShiftEntity<City>, IEntityHasCity<City>, IEntityHasRegion<City>, IEntityHasCountry<City>, IShiftEntityReplication, IShiftEntityProtectable
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
}