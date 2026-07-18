using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

[TemporalShiftEntity]
[Table("TeamBranches", Schema = "ShiftIdentity")]
public class TeamCompanyBranch : ShiftEntity<TeamCompanyBranch>
{
    public long CompanyBranchID { get; set; }
    public long TeamID { get; set; }
    public virtual CompanyBranch CompanyBranch { get; set; } = default!;
    public virtual Team Team { get; set; } = default!;
}