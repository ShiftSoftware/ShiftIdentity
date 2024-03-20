using ShiftSoftware.ShiftEntity.Model.Replication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

public class CompanyBranchBrandModel : ReplicationModel
{
    public string Name { get; set; } = default!;

    //Partition keys
    public string BranchID { get; set; } = default!;
    public string ItemType { get; set; } = default!;
}
