using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core.ReplicationModels;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("Services", Schema = "ShiftIdentity")]
[ShiftEntityReplication<ServiceModel>(ContainerName = "Services", DatabaseName = "test")]
[ReplicationPartitionKey(nameof(ServiceModel.id))]
[ReferenceReplication<CompanyBranchServiceModel>("Companies",nameof(CompanyBranchServiceModel.id), 
    nameof(CompanyBranchServiceModel.Type))]
public class Service : ShiftEntity<Service>
{
    public string Name { get; set; } = default!;
}
