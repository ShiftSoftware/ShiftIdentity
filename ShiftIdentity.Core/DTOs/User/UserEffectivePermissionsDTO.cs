namespace ShiftSoftware.ShiftIdentity.Core.DTOs.User;

public class UserEffectivePermissionsDTO
{
    /// <summary>
    /// The merged/effective access-tree JSON for a user, combining the user's
    /// user-specific access with every assigned access tree. This is the same
    /// combined set TypeAuth evaluates when validating an action for the user.
    /// </summary>
    public string AccessTree { get; set; } = "{}";
}
