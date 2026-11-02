using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

// Not replicated on its own. It is part of the team's replication tree: the team's document lists the members
// (TeamModel.UserIDs), and a save that adds, changes or deletes one of these rows marks the team as not replicated in
// the same transaction, so a failed sync leaves it to the catch-up.
[TemporalShiftEntity]
[Table("TeamUsers", Schema = "ShiftIdentity")]
public class TeamUser : ShiftEntity<TeamUser>
{
    public long UserID { get; set; }
    public long TeamID { get; set; }
    public virtual User User { get; set; } = default!;
    public virtual Team Team { get; set; } = default!;
}
