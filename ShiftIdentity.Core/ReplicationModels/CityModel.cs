using ShiftSoftware.ShiftEntity.Model.Replication;

namespace ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

public class CityModel : ReplicationModel
{
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
    public string RegionID { get; set; } = default!;
    public bool BuiltIn { get; set; }
    public string ItemType { get; set; } = default!;
}
