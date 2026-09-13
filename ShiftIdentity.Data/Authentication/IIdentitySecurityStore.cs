using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Authentication;

/// <summary>Client identity is assigned by the host, never taken from a login payload.</summary>
public sealed record AuthenticationClient(string ID, string Audience, bool External = false);

public sealed record IdentityProofSnapshot(User User, UserSecurityState Security, AuthenticationPolicyState Policy);

/// <summary>Lookup evidence must be checked again in the transaction that issues a security link.</summary>
public sealed record SecurityEmailLookup(long UserID, string Key);

/// <summary>A locked unit exposes only the security transition's participating rows.</summary>
public sealed class IdentitySecurityTransaction(
    User user, UserSecurityState security, AuthenticationPolicyState policy,
    AuthenticationOperation? operation, Action<AuthenticationOperation> addOperation,
    Action<AuthenticationAuditEvent> addAudit, IReadOnlyList<AuthenticationOperation>? recoveryFamily = null,
    IReadOnlyList<AuthenticationOperation>? links = null, App? app = null)
{
    public User User { get; } = user;
    public UserSecurityState Security { get; } = security;
    public AuthenticationPolicyState Policy { get; } = policy;
    public AuthenticationOperation? Operation { get; } = operation;
    public App? App { get; } = app;
    public IReadOnlyList<AuthenticationOperation> RecoveryFamily { get; } = recoveryFamily ?? [];
    public IReadOnlyList<AuthenticationOperation> Links { get; } = links ?? [];
    public void AddOperation(AuthenticationOperation value) => addOperation(value);
    public void Audit(string outcome, DateTimeOffset now, Guid? operationID = null, long? actorUserID = null, string? verificationReference = null) => addAudit(new()
    {
        UserID = User.ID, SecurityVersion = Security.SecurityVersion,
        OperationID = operationID, Outcome = outcome, CreatedAt = now,
        ActorUserID = actorUserID, VerificationReference = verificationReference
    });
}

/// <summary>Owns the consistent proof snapshot and SQL admission transaction, not token policy.</summary>
public interface IIdentitySecurityStore
{
    Task<IdentityProofSnapshot?> ReadProofAsync(string username, AuthenticationClient client, CancellationToken cancellationToken);
    Task<AuthenticationOperation?> ReadOperationAsync(Guid id, CancellationToken cancellationToken);
    Task<SecurityEmailLookup?> ResolveSecurityEmailAsync(string identifier, CancellationToken cancellationToken);
    Task<bool> RecheckSecurityEmailAsync(SecurityEmailLookup lookup, CancellationToken cancellationToken);
    /// <summary>Range-locked duplicate check for a proposed username or email; call only inside admission.</summary>
    Task<bool> IdentifierInUseAsync(string value, long exceptUserID, CancellationToken cancellationToken);
    Task<bool> ConsumeIngressAsync(string key, DateTimeOffset now, int limit, TimeSpan window, CancellationToken cancellationToken);
    Task<T> AdmitAsync<T>(long userID, Guid? operationID, AuthenticationClient client,
        Func<IdentitySecurityTransaction, Task<T>> transition, CancellationToken cancellationToken);
    /// <summary>Locks both source and destination Apps before the user when transferring an admitted session.</summary>
    Task<T> AdmitAppAsync<T>(long userID, AuthenticationClient source, AuthenticationClient destination,
        Func<IdentitySecurityTransaction, Task<T>> transition, CancellationToken cancellationToken);
    Task<T> AdmitAdminAsync<T>(long actorID, long userID, AuthenticationClient client,
        Func<IdentitySecurityTransaction, IdentitySecurityTransaction, Task<T>> transition, CancellationToken cancellationToken);
    /// <summary>
    /// Locks the policy, the App and the given users (ascending ID order) inside the caller's already open
    /// transaction and returns their units. Rows the caller already tracks are reused, so the admitted changes and
    /// the caller's own edits reach the database in the caller's single flush. Nothing is committed here.
    /// </summary>
    Task<IReadOnlyDictionary<long, IdentitySecurityTransaction>> AdmitWithinAsync(IEnumerable<long> userIDs,
        AuthenticationClient client, CancellationToken cancellationToken);
}

public sealed class IdentitySecurityUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class IdentitySecurityConflictException(string message, Exception? inner = null) : Exception(message, inner);
