using ShiftSoftware.ShiftEntity.Model.Replication;

namespace ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

public class CompanyBranchSubItemModel : ReplicationModel
{
    public string Name { get; set; } = default!;
    public string? ExternalId { get; set; } = default!;

    //Partition keys
    public string BranchID { get; set; } = default!;
    public string ItemType { get; set; } = default!;
}