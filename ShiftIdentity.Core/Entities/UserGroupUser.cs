using ShiftSoftware.ShiftEntity.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
