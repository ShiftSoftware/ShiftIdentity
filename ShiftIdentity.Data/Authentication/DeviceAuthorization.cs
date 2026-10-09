using ShiftSoftware.ShiftIdentity.Core.Authentication;

namespace ShiftSoftware.ShiftIdentity.Data.Authentication;

/// <summary>
/// One device sign-in (RFC 8628). The row exists before anyone approves it, so it has no account until then; that is
/// why it is not an <see cref="AuthenticationOperation"/>, whose account is required. Both codes are stored only as
/// keyed digests. The device code digest is cleared when the row delivers its session, so the code works once.
/// </summary>
public sealed class DeviceAuthorization
{
    public Guid ID { get; set; }
    /// <summary>The keyed digest of the secret device code. Null once the row is consumed or expired.</summary>
    public byte[]? DeviceCodeDigest { get; set; }
    /// <summary>The keyed digest of the public user code; unique among the rows that have one. Null once consumed or expired.</summary>
    public byte[]? UserCodeDigest { get; set; }
    /// <summary>The device client that asked: a key of the host's configured device clients, which names the screen on the phone.</summary>
    public string ClientID { get; set; } = "";
    /// <summary>The audience of the session the host issues, pinned when the device asked.</summary>
    public string Audience { get; set; } = "";
    public DeviceAuthorizationState State { get; set; }
    /// <summary>The account that approved (or denied) the code. Null while pending.</summary>
    public long? UserID { get; set; }
    // The approving account's state and its session's context, pinned at approval. The device session is issued only
    // while the account still matches them, and it carries the same context as the session that approved it.
    public long? SecurityVersion { get; set; }
    public long? PolicyRevision { get; set; }
    public long? FactorGeneration { get; set; }
    public bool? MfaSatisfied { get; set; }
    public DateTimeOffset? AuthenticatedAt { get; set; }
    public SignInProvider? SessionProvider { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? LastPolledAt { get; set; }
    /// <summary>The polling interval in seconds. A poll that arrives too early adds 5 seconds.</summary>
    public int Interval { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
