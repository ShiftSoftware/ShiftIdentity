using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Blazor;

public class ShiftIdentityBlazorOptions
{
    public string AppId { get; }
    public string BaseUrl { get; }
    public string FrontEndBaseUrl { get; }

    /// <summary>
    /// True when cookie-based auth is in use (Blazor Web App).
    /// False for standalone WASM with JWT/localStorage.
    /// </summary>
    public bool UseCookieAuth { get; init; }

    /// <summary>
    /// Whether identity is hosted in-process (Internal) or by a separate identity server (External).
    /// </summary>
    public ShiftIdentityHostingTypes HostingType { get; init; }

    /// <summary>
    /// Base URL of the external identity server. Required when <see cref="HostingType"/> is External.
    /// </summary>
    public string? ExternalIdentityApiUrl { get; init; }

    public ShiftIdentityBlazorOptions(string appId, string baseUrl, string frontEndBaseUrl)
    {
        AppId = appId;
        BaseUrl = baseUrl;
        FrontEndBaseUrl = frontEndBaseUrl;
    }
}
