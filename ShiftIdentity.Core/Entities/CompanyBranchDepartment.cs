using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Replication;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;


[TemporalShiftEntity]
[Table("CompanyBranchDepartments", Schema = "ShiftIdentity")]
public class CompanyBranchDepartment : ShiftEntity<CompanyBranchDepartment>, IShiftEntityReplication
{
    /// <inheritdoc />
    public DateTimeOffset? LastReplicationDate { get; set; }

    /// <inheritdoc />
    public string? LastReplicationStamp { get; set; }

    public long ID { get; set; }
    public long CompanyBranchID { get; set; }
    public long DepartmentID { get; set; }

    public virtual Department Department { get; set; } = default!;
    public virtual CompanyBranch CompanyBranch { get; set; } = default!;
}
