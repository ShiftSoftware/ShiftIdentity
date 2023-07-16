
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Entities;

[TemporalShiftEntity]
[Table("Regions", Schema = "ShiftIdentity")]
public class Region : ShiftEntity<Region>
{
    public string Name { get; set; } = default!;
    public string? ExternalId { get; set; } = default!;
    public string? ShortCode { get; set; }

    public virtual ICollection<CompanyBranch> CompanyBranches { get; set; }

    public Region()
    {
        this.CompanyBranches = new HashSet<CompanyBranch>();
    }

}
