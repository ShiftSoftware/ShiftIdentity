namespace ShiftSoftware.ShiftIdentity.Data.Authorization;

/// <summary>
/// The outcome of one <see cref="IdentitySubjectResolver.ResolveAsync"/> call. Every user ID asked about is in exactly
/// one of the three.
/// </summary>
public sealed class IdentitySubjectResolution
{
    internal IdentitySubjectResolution(
        IReadOnlyList<IdentityAccessSubject> eligible,
        IReadOnlyDictionary<long, IdentitySubjectNotEligibleReason> notEligible,
        IReadOnlyDictionary<long, IdentitySubjectUnresolved> unresolved)
    {
        Eligible = eligible;
        NotEligible = notEligible;
        Unresolved = unresolved;
    }

    /// <summary>
    /// The users who could sign in now, each with the claims and grants that sign-in would give them, in ascending user
    /// ID order.
    /// </summary>
    public IReadOnlyList<IdentityAccessSubject> Eligible { get; }

    /// <summary>The users who could not sign in now, with the reason. This is not an error; do not retry them.</summary>
    public IReadOnlyDictionary<long, IdentitySubjectNotEligibleReason> NotEligible { get; }

    /// <summary>
    /// The users whose access could not be worked out from the replicated documents, with the reason and a detail.
    /// Never deliver anything to them. Record them, and retry them in a later scope while the event is still valid.
    /// </summary>
    public IReadOnlyDictionary<long, IdentitySubjectUnresolved> Unresolved { get; }
}

/// <summary>Why a user could not sign in now. The same refusals as sign-in itself.</summary>
public enum IdentitySubjectNotEligibleReason
{
    /// <summary>The user is deleted.</summary>
    Deleted = 1,

    /// <summary>The user is not active.</summary>
    Inactive = 2,

    /// <summary>The user has no company, branch or region, or the branch has no city.</summary>
    NoSignInScope = 3,
}

/// <summary>Why a user's access could not be worked out from the replicated documents.</summary>
public enum IdentitySubjectUnresolvedReason
{
    /// <summary>The user has no document, even after reading it again from the store.</summary>
    UserMissing = 1,

    /// <summary>
    /// The user's document was written before a field this check needs existed (<c>IsActive</c> or
    /// <c>AccessTreeIDs</c>), or it lacks a field every user has.
    /// </summary>
    UserOutdated = 2,

    /// <summary>
    /// A team document has no member list (<c>UserIDs</c>), so no user's team claims are known for sure. This happens
    /// only between the deploy and the rewrite of the team documents.
    /// </summary>
    TeamMembersUnknown = 3,

    /// <summary>The user's company, branch or one of their named access trees has no document, even after reading it again.</summary>
    ReferenceMissing = 4,

    /// <summary>A named access tree is empty, or an access tree does not parse.</summary>
    AccessTreeInvalid = 5,
}

/// <summary>An unresolved user: the reason, and a detail that names the document or the field.</summary>
public sealed class IdentitySubjectUnresolved
{
    internal IdentitySubjectUnresolved(IdentitySubjectUnresolvedReason reason, string detail)
    {
        Reason = reason;
        Detail = detail;
    }

    public IdentitySubjectUnresolvedReason Reason { get; }

    public string Detail { get; }

    public override string ToString() => $"{Reason}: {Detail}";
}
