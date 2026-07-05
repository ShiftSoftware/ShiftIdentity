using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Flags;
using ShiftSoftware.ShiftEntity.Model.Replication;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("Regions", Schema = "ShiftIdentity")]
public class Region : ShiftEntity<Region>, IEntityHasCountry<Region>, IEntityHasRegion<Region>, IShiftEntityReplication
{
    /// <inheritdoc />
    public DateTimeOffset? LastReplicationDate { get; set; }

    /// <inheritdoc />
    public string? LastReplicationStamp { get; set; }

    [Column(TypeName = "json")]
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
    public string? ShortCode { get; set; }
    public bool BuiltIn { get; set; }
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
