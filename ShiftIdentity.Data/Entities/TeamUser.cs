using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

// Not replicated on its own. The team's document lists the members (TeamModel.UserIDs), and every change to these
// rows is saved together with the team row (Team.UpsertAsync), so a failed sync leaves the team row dirty and the
// catch-up rewrites the whole list.
[TemporalShiftEntity]
[Table("TeamUsers", Schema = "ShiftIdentity")]
public class TeamUser : ShiftEntity<TeamUser>
{
    public long UserID { get; set; }
    public long TeamID { get; set; }
    public virtual User User { get; set; } = default!;
    public virtual Team Team { get; set; } = default!;
}
