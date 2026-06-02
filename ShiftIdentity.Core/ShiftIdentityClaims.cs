namespace ShiftSoftware.ShiftIdentity.Core;

public static class ShiftIdentityClaims
{
    public const string Scope = "Scope";
    public const string RequirePasswordChange = "RequirePasswordChange";

    /// <summary>
    /// Carried by refresh tokens only. Holds the user's <c>SecurityStamp</c> at mint time so a
    /// refresh can be rejected once the stamp rolls (password change / log-out-everywhere).
    /// </summary>
    public const string SecurityStamp = "SecurityStamp";
}
