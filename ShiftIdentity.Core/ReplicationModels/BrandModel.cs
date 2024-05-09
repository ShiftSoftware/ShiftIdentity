using ShiftSoftware.ShiftEntity.Model.Replication;

namespace ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

public class BrandModel : ReplicationModel
{
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; } = default!;
}
