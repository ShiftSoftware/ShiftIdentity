using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("TeamUsers", Schema = "ShiftIdentity")]
public class TeamUser : ShiftEntity<TeamUser>
{
    public long UserID { get; set; }
    public long TeamID { get; set; }
    public virtual User User { get; set; } = default!;
    public virtual Team Team { get; set; } = default!;
}
