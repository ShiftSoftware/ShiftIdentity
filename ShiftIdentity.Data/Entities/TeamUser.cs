using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations.Schema;
using ShiftSoftware.ShiftEntity.Model.Replication;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

[TemporalShiftEntity]
[Table("TeamUsers", Schema = "ShiftIdentity")]
public class TeamUser : ShiftEntity<TeamUser>, IShiftEntityReplication
{
    public DateTimeOffset? LastReplicationDate { get; set; }
    public string? LastReplicationStamp { get; set; }

    public long UserID { get; set; }
    public long TeamID { get; set; }
    public virtual User User { get; set; } = default!;
    public virtual Team Team { get; set; } = default!;
}
