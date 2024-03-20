using ShiftSoftware.ShiftEntity.Model.Replication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

public class BrandModel : ReplicationModel
{
    public string Name { get; set; } = default!;
}
