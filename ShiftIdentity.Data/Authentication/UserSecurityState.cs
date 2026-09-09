namespace ShiftSoftware.ShiftIdentity.Data.Authentication;

/// <summary>Authoritative, non-temporal state. Not part of CRUD DTOs or replication.</summary>
public sealed class UserSecurityState
{
    public long UserID { get; set; }
    public long SecurityVersion { get; set; } = 1;
    public long ContactRevision { get; set; } = 1;
    public string? UsernameLookupKey { get; set; }
    public string? EmailLookupKey { get; set; }
    public string? RecoveryEmail { get; set; }
    public long? RecoveryEmailRevision { get; set; }
    public RecoveryEmailProvenance RecoveryEmailProvenance { get; set; }
    public DateTimeOffset? DeliveryWindowStart { get; set; }
    public DateTimeOffset? LastDeliveryAt { get; set; }
    public int DeliveryCount { get; set; }
    public long FactorGeneration { get; set; } = 1;
    public bool LocalMfaRecoveryRequired { get; set; }
    public byte[]? ProtectedTotpSecret { get; set; }
    public int TotpProtectionVersion { get; set; }
    public Guid? MfaRecoveryOperationID { get; set; }
    public long? LastAcceptedTotpStep { get; set; }
    public int FailedProofs { get; set; }
    public DateTimeOffset? FailureWindowStart { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
