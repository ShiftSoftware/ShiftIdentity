using ShiftSoftware.ShiftEntity.Model.Dtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

public class CompanyBranchServiceModel : ShiftEntityDTO
{
    public override string? ID { get; set; }
    public long CompanyBranchID { get; set; }
    public string Type { get; set; }
    public string Name { get; set; } = default!;
}
