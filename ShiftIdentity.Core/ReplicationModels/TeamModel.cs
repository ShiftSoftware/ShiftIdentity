using ShiftSoftware.ShiftEntity.Model.Replication;

namespace ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

public class TeamModel : ReplicationModel
{
    public string Name { get; set; }

    public string? IntegrationId { get; set; }
}
