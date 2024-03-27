using ShiftSoftware.ShiftEntity.Model.Replication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

public class CityCompanyBranchModel : ReplicationModel
{
    public string Name { get; set; } = default!;
    public string? ExternalId { get; set; }
    public bool BuiltIn { get; set; }
    public RegionModel Region { get; set; }
}
