using System.Security.Claims;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Core.DataLevelAccess;
using ShiftSoftware.TypeAuth.Core;

namespace ShiftSoftware.ShiftIdentity.Data.Authorization;

/// <summary>
/// One active replicated user, with private TypeAuth grants and claims. Use for a bounded lookup/batch;
/// reload subjects to observe subsequent replication updates. No HTTP principal is read or replaced.
/// Contains identity scope claims, not authentication-session claims or proof of a signed-in session.
/// </summary>
public sealed class IdentityAccessSubject : ICurrentUserProvider
{
    private readonly ClaimsPrincipal principal;
    private readonly DataLevelAccessContext dataContext;

    /// <summary>The raw identity document id. Encode it with UserDTO when crossing a hashed-id API boundary.</summary>
    public string UserId { get; }
    public ITypeAuthService TypeAuth { get; }

    internal IdentityAccessSubject(string userId, TypeAuthContext typeAuth, IEnumerable<Claim> claims, IHashIdService hashIds)
    {
        UserId = userId;
        TypeAuth = typeAuth;
        principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "IdentityReplication"));
        dataContext = new(new TypeAuthAccessibleItemsSource(typeAuth), this, hashIds);
    }

    // Return a copy so a consumer cannot change claims captured by this subject's data context.
    public ClaimsPrincipal GetUser() => new(principal.Identities.Select(identity => new ClaimsIdentity(identity)));

    /// <summary>
    /// Requires both system access and the supplied effective repository policy at the same operation.
    /// The system predicate supports static/dynamic actions and candidate-specific self ids through TypeAuth.
    /// Pass the repository's effective policy, including its standard dimensions and overrides.
    /// Application rules outside that policy must also be enforced by the consumer.
    /// </summary>
    public bool CanAccess<TEntity>(TEntity entity, Access operation, DataLevelAccessPolicy<TEntity> policy,
        Func<IdentityAccessSubject, Access, bool> hasSystemAccess)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(hasSystemAccess);
        ArgumentNullException.ThrowIfNull(entity);
        return hasSystemAccess(this, operation) && policy.Authorize(entity, operation, dataContext);
    }
}
