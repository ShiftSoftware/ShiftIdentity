using System.Security.Claims;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.TypeAuth.Core;

namespace ShiftSoftware.ShiftIdentity.Data.Authorization;

/// <summary>
/// One user who could sign in now, prepared from the replicated Identity documents by
/// <see cref="IdentitySubjectResolver"/>: the TypeAuth grants and the claims that sign-in would give this user's
/// session. It does not sign the user in and is not a token.
/// <para>
/// To ask whether this user could open a record, pass the subject to a repository's
/// <c>CanAccessAs(entity, access, subject.TypeAuth, subject)</c> for the data-level rules, and check the screen
/// permission with <c>subject.TypeAuth.Can(...)</c>.
/// </para>
/// </summary>
public sealed class IdentityAccessSubject : ICurrentUserProvider
{
    private readonly ClaimsPrincipal principal;

    /// <summary>The user's ID, as stored. Encode it with <c>UserDTO</c> before it crosses a hashed-ID API boundary.</summary>
    public long UserId { get; }

    /// <summary>The user's grants: the direct access tree and the assigned named trees, merged.</summary>
    public ITypeAuthService TypeAuth { get; }

    internal IdentityAccessSubject(long userId, TypeAuthContext typeAuth, IEnumerable<Claim> claims)
    {
        UserId = userId;
        TypeAuth = typeAuth;
        // An authentication type makes the identity authenticated, so data-level rules read its claims.
        principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "IdentityReplication"));
    }

    /// <summary>
    /// The claims that sign-in would put on this user's session. Each call returns a copy, so a caller cannot change
    /// the subject.
    /// </summary>
    public ClaimsPrincipal GetUser() => new(principal.Identities.Select(identity => new ClaimsIdentity(identity)));
}
