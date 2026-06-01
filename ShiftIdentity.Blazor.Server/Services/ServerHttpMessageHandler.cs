using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ShiftSoftware.ShiftBlazor.Services;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Services;

/// <summary>
/// Forwards the auth cookie from the incoming HttpContext to outgoing HttpClient requests.
/// Needed during server-side rendering (SSR) where HttpClient calls to the same
/// origin don't automatically include the user's cookies.
///
/// The cookie is only forwarded to hosts the app is explicitly configured to talk to:
/// ShiftBlazor's <see cref="AppConfiguration.BaseAddress"/> and
/// <see cref="AppConfiguration.ExternalAddresses"/>, plus the identity server and
/// front-end URLs passed to <c>AddShiftIdentityBlazorServer</c>. This prevents the
/// session cookie from leaking to arbitrary third-party hosts (analytics, geocoding,
/// etc.) should this handler ever be attached to an HttpClient that calls them.
/// </summary>
public class ServerHttpMessageHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly HashSet<string> _allowedHosts;

    public ServerHttpMessageHandler(
        IHttpContextAccessor httpContextAccessor,
        IOptions<AppConfiguration> appConfiguration,
        ShiftIdentity.Blazor.ShiftIdentityBlazorOptions identityOptions)
    {
        _httpContextAccessor = httpContextAccessor;

        var config = appConfiguration.Value;

        var candidates = new List<string?>
        {
            config.BaseAddress,
            identityOptions.BaseUrl,
            identityOptions.FrontEndBaseUrl,
        };
        candidates.AddRange(config.ExternalAddresses.Values);

        _allowedHosts = candidates
            .Select(TryGetHost)
            .Where(host => !string.IsNullOrEmpty(host))
            .Select(host => host!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null && IsAllowedHost(request.RequestUri))
        {
            var cookieHeader = httpContext.Request.Headers["Cookie"].ToString();
            if (!string.IsNullOrEmpty(cookieHeader))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// True only when the request targets an absolute URL whose host is in the configured
    /// allowlist. Relative URIs (no BaseAddress on the client) and unconfigured hosts are
    /// rejected so the cookie is never forwarded to an unintended destination.
    /// </summary>
    private bool IsAllowedHost(Uri? requestUri)
    {
        if (requestUri is null || !requestUri.IsAbsoluteUri)
            return false;

        return _allowedHosts.Contains(requestUri.Host);
    }

    private static string? TryGetHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;
    }
}
