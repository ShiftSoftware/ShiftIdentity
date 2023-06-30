namespace ShiftSoftware.ShiftIdentity.AspNetCore.Models;

public class SecuritySettingsModel
{
    public int LoginAttemptsForLockDown { get; set; }

    public int LockDownInMinutes { get; set; }

    public bool RequirePasswordChange { get; set; }
}
