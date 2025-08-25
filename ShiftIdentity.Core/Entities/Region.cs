using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Flags;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("Regions", Schema = "ShiftIdentity")]
public class Region : ShiftEntity<Region>, IEntityHasCountry<Region>, IEntityHasRegion<Region>
{
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
    public string? ShortCode { get; set; }
    public bool BuiltIn { get; set; }
    public long? CountryID { get; set; }
    public virtual Country? Country { get; set; }
    public long? RegionID { get; set; }

    public virtual ICollection<CompanyBranch> CompanyBranches { get; set; }

    public Region()
    {
        CompanyBranches = new HashSet<CompanyBranch>();
    }

}
