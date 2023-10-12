using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core.ReplicationModels;
using System.ComponentModel.DataAnnotations.Schema;


namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("Departments", Schema = "ShiftIdentity")]
[ShiftEntityReplication<DepartmentModel>(ContainerName = ReplicationConfiguration.DepartmentContainerName, 
    AccountName = ReplicationConfiguration.AccountName)]
[ReplicationPartitionKey(nameof(DepartmentModel.id))]
[ReferenceReplication<CompanyBranchDepartmentModel>(ReplicationConfiguration.CompanyContainerName, 
    nameof(CompanyBranchDepartmentModel.id), nameof(CompanyBranchDepartmentModel.ItemType))]
public class Department : ShiftEntity<Department>
{
    public string Name { get; set; } = default!;

}
