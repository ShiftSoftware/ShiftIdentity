namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services;

/// <summary>
/// The raw HTTP client behind the dashboard's staged security flows: based at the identity API root and without
/// the ordinary bearer handler, because the flow sets its own bearer and operation credentials on every request.
/// Registered by <c>AddShiftIdentityDashboardBlazor</c> when <see cref="ShiftIdentityDashboardBlazorOptions.StagedAuthority"/>
/// is on; a host that registers its own instance first (a test, the development app) keeps it.
/// </summary>
public sealed class StagedAuthorityHttpClient : HttpClient
{
    public StagedAuthorityHttpClient() { }
    public StagedAuthorityHttpClient(HttpMessageHandler handler) : base(handler) { }

    /// <summary>
    /// The identity API root for the configured identity API base URL. The dashboard's base URL ends with the
    /// <c>api/</c> segment its relative routes live under (<c>https://identity.example/api/</c>); the staged routes
    /// are addressed from the root above it (<c>https://identity.example/api/identity/v2/…</c>). A base URL without
    /// that segment is taken as the root itself.
    /// </summary>
    public static Uri ApiRootOf(string identityApiBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(identityApiBaseUrl))
            throw new ArgumentException("The identity API base URL is required.", nameof(identityApiBaseUrl));
        var url = identityApiBaseUrl.EndsWith('/') ? identityApiBaseUrl : identityApiBaseUrl + "/";
        if (url.EndsWith("/api/", StringComparison.OrdinalIgnoreCase)) url = url[..^4];
        return new Uri(url, UriKind.Absolute);
    }
}
