using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Core.Flags;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;


[TemporalShiftEntity]
[Table("Cities", Schema = "ShiftIdentity")]
public class City : ShiftEntity<City>, IEntityHasRegion<City>, IEntityHasCountry<City>
{
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
    public long? RegionID { get; set; }
    public virtual Region? Region { get; set; } = default!;
    public bool BuiltIn { get; set; }
    public virtual ICollection<CompanyBranch> CompanyBranches { get; set; }
    public long? CountryID { get; set; }

    public City()
    {
        CompanyBranches = new HashSet<CompanyBranch>();
    }
}