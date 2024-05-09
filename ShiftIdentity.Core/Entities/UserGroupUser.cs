using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("UserGroupUsers", Schema = "ShiftIdentity")]
public class UserGroupUser : ShiftEntity<UserGroupUser>
{
    public long UserID { get; set; }
    public long UserGroupID { get; set; }
    public virtual User User { get; set; } = default!;
    public virtual UserGroup UserGroup { get; set; } = default!;
}
