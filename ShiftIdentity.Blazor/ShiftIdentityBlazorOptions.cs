namespace ShiftSoftware.ShiftIdentity.Blazor;

public class ShiftIdentityBlazorOptions
{
    public string AppId { get; private set; }
    public string BaseUrl { get; private set; }
    public string FrontEndBaseUrl { get; private set; }
    public bool NoNeedAuthCode { get; set; }

    /// <summary>
    /// Where the refresh token is kept. Use <see cref="RefreshTokenStorage.Cookie"/> together with
    /// <see cref="CookieDomain"/> to share a session across apps on the same parent domain.
    /// </summary>
    public RefreshTokenStorage RefreshTokenStorage { get; set; } = RefreshTokenStorage.LocalStorage;

    /// <summary>
    /// Domain the refresh token cookie is written to, e.g. "toyota.iq" to share it with sibling apps.
    /// Only used when <see cref="RefreshTokenStorage"/> is <see cref="RefreshTokenStorage.Cookie"/>.
    /// </summary>
    public string? CookieDomain { get; set; }

    public ShiftIdentityBlazorOptions(string appId, string baseUrl, string frontEndBaseUrl, bool noNeedAuthCode)
    {
        AppId = appId;
        BaseUrl = baseUrl;
        FrontEndBaseUrl = frontEndBaseUrl;
        NoNeedAuthCode = noNeedAuthCode;
    }
}

public enum RefreshTokenStorage
{
    /// <summary>Refresh token is kept in local storage, scoped to this app's origin.</summary>
    LocalStorage,

    /// <summary>Refresh token is kept in a cookie, so apps sharing the cookie domain share the session.</summary>
    Cookie
}
