using ShiftSoftware.ShiftEntity.Model.Replication;

namespace ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

public class CityCompanyBranchModel : ReplicationModel
{
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
    public bool BuiltIn { get; set; }
    public RegionModel Region { get; set; }
}
