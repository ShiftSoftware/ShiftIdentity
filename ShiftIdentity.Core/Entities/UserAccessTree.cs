using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("UserAccessTrees", Schema = "ShiftIdentity")]
public class UserAccessTree: ShiftEntity<UserAccessTree>
{
    public long UserID { get; set; }

    public long AccessTreeID { get; set; }

    public virtual User User { get; set; } = default!;

    public virtual AccessTree AccessTree { get; set; } = default!;
}
