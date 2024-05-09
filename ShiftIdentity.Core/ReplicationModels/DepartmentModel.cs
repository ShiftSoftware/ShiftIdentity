using ShiftSoftware.ShiftEntity.Model.Replication;

namespace ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

public class DepartmentModel : ReplicationModel
{
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
}
