namespace ShiftSoftware.ShiftIdentity.Core.Models
{
    public class SecuritySettingsModel
    {
        public int LoginAttemptsForLockDown { get; set; }

        public int LockDownInMinutes { get; set; }

        public bool RequirePasswordChange { get; set; }
    }
}
