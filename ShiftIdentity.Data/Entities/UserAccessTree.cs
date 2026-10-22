using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

// Not replicated on its own. The user's document lists the assigned trees (UserModel.AccessTreeIDs), and every change
// to these rows is saved together with the user row (User.UpsertAsync), so a failed sync leaves the user row dirty
// and the catch-up rewrites the whole list.
[TemporalShiftEntity]
[Table("UserAccessTrees", Schema = "ShiftIdentity")]
public class UserAccessTree: ShiftEntity<UserAccessTree>
{
    public long UserID { get; set; }

    public long AccessTreeID { get; set; }

    public virtual User User { get; set; } = default!;

    public virtual AccessTree AccessTree { get; set; } = default!;
}
