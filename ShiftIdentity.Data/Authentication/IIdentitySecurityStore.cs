using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Authentication;

/// <summary>Client identity is assigned by the host, never taken from a login payload.</summary>
public sealed record AuthenticationClient(string ID, string Audience, bool External = false);

public sealed record IdentityProofSnapshot(User User, UserSecurityState Security, AuthenticationPolicyState Policy);

/// <summary>A locked unit exposes only the security transition's participating rows.</summary>
public sealed class IdentitySecurityTransaction(
    User user, UserSecurityState security, AuthenticationPolicyState policy,
    AuthenticationOperation? operation, Action<AuthenticationOperation> addOperation,
    Action<AuthenticationAuditEvent> addAudit)
{
    public User User { get; } = user;
    public UserSecurityState Security { get; } = security;
    public AuthenticationPolicyState Policy { get; } = policy;
    public AuthenticationOperation? Operation { get; } = operation;
    public void AddOperation(AuthenticationOperation value) => addOperation(value);
    public void Audit(string outcome, DateTimeOffset now, Guid? operationID = null) => addAudit(new()
    {
        UserID = User.ID, SecurityVersion = Security.SecurityVersion,
        OperationID = operationID, Outcome = outcome, CreatedAt = now
    });
}

/// <summary>Owns the consistent proof snapshot and SQL admission transaction, not token policy.</summary>
public interface IIdentitySecurityStore
{
    Task<IdentityProofSnapshot?> ReadProofAsync(string username, AuthenticationClient client, CancellationToken cancellationToken);
    Task<AuthenticationOperation?> ReadOperationAsync(Guid id, CancellationToken cancellationToken);
    Task<T> AdmitAsync<T>(long userID, Guid? operationID, AuthenticationClient client,
        Func<IdentitySecurityTransaction, Task<T>> transition, CancellationToken cancellationToken);
}

public sealed class IdentitySecurityUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class IdentitySecurityConflictException(string message, Exception? inner = null) : Exception(message, inner);
