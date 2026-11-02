using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

// Not replicated on its own. It is part of the user's replication tree: the user's document lists the assigned trees
// (UserModel.AccessTreeIDs), and a save that adds, changes or deletes one of these rows marks the user as not
// replicated in the same transaction, so a failed sync leaves it to the catch-up.
[TemporalShiftEntity]
[Table("UserAccessTrees", Schema = "ShiftIdentity")]
public class UserAccessTree: ShiftEntity<UserAccessTree>
{
    public long UserID { get; set; }

    public long AccessTreeID { get; set; }

    public virtual User User { get; set; } = default!;

    public virtual AccessTree AccessTree { get; set; } = default!;
}
