using ShiftSoftware.ShiftEntity.Model.Replication;

namespace ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

public class CompanyBranchServiceModel : ReplicationModel
{
    public override string? ID { get; set; }

    public string Name { get; set; } = default!;

    //Partition keys
    public string CompanyID { get; set; } = default!;
    public string BranchID { get; set; } = default!;
    public string ItemType { get; set; } = default!;
}
