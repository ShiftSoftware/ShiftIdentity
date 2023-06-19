using ShiftSoftware.TypeAuth.Core;
using ShiftSoftware.TypeAuth.Core.Actions;

namespace ShiftSoftware.ShiftIdentity.Core;

[ActionTree("Identity", "Users, And Access")]
public class ShiftIdentityActions
{
    public readonly static ReadWriteDeleteAction Apps = new ReadWriteDeleteAction("Apps");
    public readonly static ReadWriteDeleteAction AccessTrees = new ReadWriteDeleteAction("Access Trees");
    public readonly static ReadWriteDeleteAction Users = new ReadWriteDeleteAction("Users");
}
