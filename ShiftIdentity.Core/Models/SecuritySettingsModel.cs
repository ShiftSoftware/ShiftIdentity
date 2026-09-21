namespace ShiftSoftware.ShiftIdentity.Core.Models;

public class SecuritySettingsModel
{
    public int LoginAttemptsForLockDown { get; set; }

    public int LockDownInMinutes { get; set; }

    public bool RequirePasswordChange { get; set; }

    /// <summary>
    /// For legacy clients that cannot complete interactive steps. Login and refresh refuse accounts requiring
    /// password change, an authenticator or MFA recovery. Does not change token lifetimes or authority policy.
    /// </summary>
    public bool PasswordOnly { get; set; }
}
