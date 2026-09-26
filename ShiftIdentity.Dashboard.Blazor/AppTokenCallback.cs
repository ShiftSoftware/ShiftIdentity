namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor;

/// <summary>
/// The address the sign-in page sends the browser to after Allow: the app's registered RedirectUri followed by the
/// client's token route (ShiftIdentity.Blazor's Token component, "/Auth/Token"). Only the built address is trimmed. The
/// stored RedirectUri stays as registered, because the app-code binding hashes it and trimming the row would end the
/// app's live sessions.
/// </summary>
internal static class AppTokenCallback
{
    public const string Route = "/Auth/Token";

    public static string For(string redirectUri) => redirectUri.TrimEnd('/') + Route;
}
