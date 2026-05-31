namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Models;

public class ShiftIdentityCookieAuthOptions
{
    /// <summary>
    /// Name of the authentication cookie.
    /// </summary>
    public string CookieName { get; set; } = ".ShiftIdentity.Auth";

    /// <summary>
    /// How long the auth cookie remains valid.
    /// </summary>
    public TimeSpan ExpireTimeSpan { get; set; } = TimeSpan.FromDays(3);
}
