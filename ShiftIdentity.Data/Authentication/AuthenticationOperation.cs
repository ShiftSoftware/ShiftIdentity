using ShiftSoftware.ShiftIdentity.Core.Authentication;

namespace ShiftSoftware.ShiftIdentity.Data.Authentication;

public enum AuthenticationOperationState { AwaitingMfa = 1, Completed = 2, Locked = 3, AwaitingPassword = 4, AwaitingNewPassword = 5, Cancelled = 6 }
public enum PasswordChangeOrigin { Voluntary = 1, RequiredLogin = 2 }

/// <summary>A single-use, purpose-bound continuation; it is never an ordinary session credential.</summary>
public sealed class AuthenticationOperation
{
    public Guid ID { get; set; }
    public long UserID { get; set; }
    public AuthenticationOperationPurpose Purpose { get; set; }
    public AuthenticationOperationState State { get; set; }
    public long SecurityVersion { get; set; }
    public long FactorGeneration { get; set; }
    public long PolicyRevision { get; set; }
    public string ClientID { get; set; } = "";
    public string Audience { get; set; } = "";
    public bool External { get; set; }
    public byte[] HandleDigest { get; set; } = [];
    public string CodeChallenge { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public PasswordChangeOrigin? PasswordChangeOrigin { get; set; }
    public DateTimeOffset? PasswordProvenAt { get; set; }
    public DateTimeOffset? MfaProvenAt { get; set; }
    // Only prepared adaptive hashes are retained while awaiting MFA, never plaintext passwords.
    public byte[]? PendingPasswordHash { get; set; }
    public byte[]? PendingPasswordSalt { get; set; }
    public int FailedAttempts { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class AuthenticationPolicyState
{
    public int ID { get; set; } = 1;
    public long Revision { get; set; } = 1;
    public bool MfaEnabled { get; set; } = true;
    public bool MfaMandatory { get; set; }
    public bool RequireVerifiedEmail { get; set; }
    public int TotpDigits { get; set; } = 6;
    public int TotpPeriodSeconds { get; set; } = 30;
    public int TotpWindowPast { get; set; } = 1;
    public int TotpWindowFuture { get; set; } = 1;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class AuthenticationAuditEvent
{
    public Guid ID { get; set; } = Guid.NewGuid();
    public long UserID { get; set; }
    public Guid? OperationID { get; set; }
    public long SecurityVersion { get; set; }
    public string Outcome { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
