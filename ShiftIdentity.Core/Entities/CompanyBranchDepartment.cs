using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;


[TemporalShiftEntity]
[Table("CompanyBranchDepartments", Schema = "ShiftIdentity")]
public class CompanyBranchDepartment
{
    public long ID { get; set; }
    public long CompanyBranchID { get; set; }
    public long DepartmentID { get; set; }

    public virtual Department Department { get; set; } = default!;
    public virtual CompanyBranch CompanyBranch { get; set; } = default!;
}
