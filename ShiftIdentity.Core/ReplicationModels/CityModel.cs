using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

public class CityModel : ReplicationModel
{
    public string Name { get; set; } = default!;
    public string? ExternalId { get; set; }
    public string RegionID { get; set; } = default!;
    public bool BuiltIn { get; set; }
    public string ItemType { get; set; } = default!;
}
