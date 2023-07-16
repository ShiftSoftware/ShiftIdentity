using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Entities;

[TemporalShiftEntity]
[Table("CompanyBranches", Schema = "ShiftIdentity")]
public class CompanyBranch : ShiftEntity<CompanyBranch>
{
    public string Name { get; set; } = default!;
    public long CompanyID { get; set; }
    public long RegionID { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? ExternalId { get; set; } = default!;
    public string? ShortCode { get; set; }
    public virtual Company Company { get; set; } = default!;
    public virtual Region Region { get; set; } = default!;
    public virtual ICollection<CompanyBranchDepartment> CompanyBranchDepartments { get; set; }
    public virtual ICollection<CompanyBranchService> CompanyBranchServices { get; set; }

    public CompanyBranch()
    {
        this.CompanyBranchDepartments = new HashSet<CompanyBranchDepartment>();
        this.CompanyBranchServices = new HashSet<CompanyBranchService>();
    }

}
