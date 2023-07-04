
using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations.Schema;


namespace ShiftSoftware.ShiftIdentity.AspNetCore.Entities;

[TemporalShiftEntity]
[Table("CompanyBranchServices", Schema = "ShiftIdentity")]
public class CompanyBranchService
{
    public long ID { get; set; }
    public long CompanyBranchID { get; set; }
    public long ServiceID { get; set; }

    public virtual Service Service { get; set; } = default!;
    public virtual CompanyBranch CompanyBranch { get; set; } = default!;
}
