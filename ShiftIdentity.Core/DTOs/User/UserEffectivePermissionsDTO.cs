using System.Collections.Generic;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.User;

public class UserEffectivePermissionsDTO
{
    /// <summary>
    /// The user's access trees: their user-specific access and every assigned access tree, as sign-in puts them in the
    /// token. TypeAuth decides with all of them as one context built from this list. They aren't merged into one tree:
    /// one tree can't hold everything several grant together (rows beyond a dynamic action's wildcard).
    /// </summary>
    public List<string> AccessTrees { get; set; } = new();
}
