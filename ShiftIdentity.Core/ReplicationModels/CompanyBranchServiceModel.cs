using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.Replication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

public class CompanyBranchServiceModel : ReplicationBaseModel
{
    public long CompanyBranchID { get; set; }
    public string Type { get; set; }
    public string Name { get; set; } = default!;
}
