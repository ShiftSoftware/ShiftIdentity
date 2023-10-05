using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core.ReplicationModels;
using System.ComponentModel.DataAnnotations.Schema;


namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("CompanyBranchServices", Schema = "ShiftIdentity")]
[ShiftEntityReplication<CompanyBranchServiceModel>(ContainerName = "CompanyBranch", DatabaseName = "test")]
[ReplicationPartitionKey(nameof(CompanyBranchServiceModel.CompanyBranchID), nameof(CompanyBranchServiceModel.Type))]
public class CompanyBranchService : ShiftEntityBase<CompanyBranchService>
{
    public long ID { get; set; }
    public long CompanyBranchID { get; set; }
    public long ServiceID { get; set; }

    public virtual Service Service { get; set; } = default!;
    public virtual CompanyBranch CompanyBranch { get; set; } = default!;
}
