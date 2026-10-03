using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations.Schema;
using ShiftSoftware.ShiftEntity.Model.Replication;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

[TemporalShiftEntity]
[Table("UserAccessTrees", Schema = "ShiftIdentity")]
public class UserAccessTree: ShiftEntity<UserAccessTree>, IShiftEntityReplication
{
    public DateTimeOffset? LastReplicationDate { get; set; }
    public string? LastReplicationStamp { get; set; }

    public long UserID { get; set; }

    public long AccessTreeID { get; set; }

    public virtual User User { get; set; } = default!;

    public virtual AccessTree AccessTree { get; set; } = default!;
}
