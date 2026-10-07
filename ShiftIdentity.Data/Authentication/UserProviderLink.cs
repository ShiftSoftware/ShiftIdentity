using ShiftSoftware.ShiftIdentity.Core.Authentication;

namespace ShiftSoftware.ShiftIdentity.Data.Authentication;

/// <summary>
/// A sign-in provider identity that has signed in to an account. The first sign-in matches the account by the email the
/// provider verified; later ones find it through this row. The row holds only while the account keeps the email it
/// matched (<see cref="EmailLookupKey"/>): an email change makes it stale, and the next sign-in matches by email again.
/// Not part of CRUD DTOs or replication.
/// </summary>
public sealed class UserProviderLink
{
    public Guid ID { get; set; }
    public long UserID { get; set; }
    public SignInProvider Provider { get; set; }
    /// <summary>The provider's directory of the identity: the Microsoft tenant ID (<c>tid</c>), or Google's issuer.</summary>
    public string TenantID { get; set; } = "";
    /// <summary>The provider's stable identifier of the person in that directory: the Microsoft object ID (<c>oid</c>) or Google's <c>sub</c>.</summary>
    public string ObjectID { get; set; } = "";
    /// <summary>The account's email lookup key when this link was made.</summary>
    public string EmailLookupKey { get; set; } = "";
    /// <summary>The email the provider vouched for when this link was made, for display.</summary>
    public string Email { get; set; } = "";
    /// <summary>A personal account (a personal Microsoft account, or a Google account outside Workspace) rather than a work or school one.</summary>
    public bool PersonalAccount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
}
