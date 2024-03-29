using ShiftSoftware.ShiftEntity.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[Table("UserLogs", Schema = "ShiftIdentity")]
[DontSetCompanyInfoOnThisEntityWithAutoTrigger]
public class UserLog : ShiftEntity<UserLog>
{
    public DateTimeOffset? LastSeen { get; set; }
    public long UserID { get; set; }
    public virtual User User { get; set; }
}
