using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core.ReplicationModels;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;


[TemporalShiftEntity]
[Table("CompanyBranchDepartments", Schema = "ShiftIdentity")]
[ShiftEntityReplication<CompanyBranchDepartmentModel>(ContainerName = ReplicationConfiguration.CompanyContainerName,
    AccountName = ReplicationConfiguration.AccountName)]
[ReplicationPartitionKey(nameof(CompanyBranchDepartmentModel.CompanyID), nameof(CompanyBranchDepartmentModel.BranchID),
    nameof(CompanyBranchDepartmentModel.ItemType))]
public class CompanyBranchDepartment : ShiftEntity<CompanyBranchDepartment>
{
    public long ID { get; set; }
    public long CompanyBranchID { get; set; }
    public long DepartmentID { get; set; }

    public virtual Department Department { get; set; } = default!;
    public virtual CompanyBranch CompanyBranch { get; set; } = default!;
}
