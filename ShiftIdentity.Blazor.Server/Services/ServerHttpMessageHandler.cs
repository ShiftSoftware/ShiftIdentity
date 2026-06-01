using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ShiftSoftware.ShiftBlazor.Services;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Models;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Services;

/// <summary>
/// Forwards the auth cookie from the incoming HttpContext to outgoing HttpClient requests.
/// Needed during server-side rendering (SSR) where HttpClient calls to the same
/// origin don't automatically include the user's cookies.
///
/// Two safeguards keep the session from leaking:
/// <list type="bullet">
/// <item>The cookie is only forwarded to hosts the app is explicitly configured to talk to:
/// ShiftBlazor's <see cref="AppConfiguration.BaseAddress"/> and
/// <see cref="AppConfiguration.ExternalAddresses"/>, plus the identity server and
/// front-end URLs passed to <c>AddShiftIdentityBlazorServer</c>.</item>
/// <item>Only the ShiftIdentity auth cookie (<see cref="ShiftIdentityCookieAuthOptions.CookieName"/>
/// and its <c>ChunkingCookieManager</c> overflow chunks) is forwarded — never the rest of the
/// browser's cookies. So even a legitimately-configured external host (e.g. an identity API on
/// a different domain) receives nothing but the auth cookie.</item>
/// </list>
/// </summary>
public class ServerHttpMessageHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _cookieName;
    private readonly HashSet<string> _allowedHosts;

    public ServerHttpMessageHandler(
        IHttpContextAccessor httpContextAccessor,
        IOptions<AppConfiguration> appConfiguration,
        ShiftIdentity.Blazor.ShiftIdentityBlazorOptions identityOptions,
        ShiftIdentityCookieAuthOptions cookieAuthOptions)
    {
        _httpContextAccessor = httpContextAccessor;
        _cookieName = cookieAuthOptions.CookieName;

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
            var authCookie = BuildAuthCookieHeader(httpContext.Request.Cookies);
            if (!string.IsNullOrEmpty(authCookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", authCookie);
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

    /// <summary>
    /// Rebuilds a <c>Cookie</c> header containing only the auth cookie and its overflow chunks,
    /// dropping every other cookie the browser sent.
    /// </summary>
    private string? BuildAuthCookieHeader(IRequestCookieCollection cookies)
    {
        var parts = cookies
            .Where(c => IsAuthCookie(c.Key))
            .Select(c => $"{c.Key}={c.Value}");

        var header = string.Join("; ", parts);
        return header.Length == 0 ? null : header;
    }

    /// <summary>
    /// Matches the auth cookie itself and the <c>ChunkingCookieManager</c> overflow chunks it
    /// produces for large cookies, named <c>{CookieName}C1</c>, <c>{CookieName}C2</c>, …
    /// Cookie names are case-sensitive, so the comparison is ordinal.
    /// </summary>
    private bool IsAuthCookie(string name)
    {
        if (string.Equals(name, _cookieName, StringComparison.Ordinal))
            return true;

        if (name.Length > _cookieName.Length + 1
            && name.StartsWith(_cookieName, StringComparison.Ordinal)
            && name[_cookieName.Length] == 'C')
        {
            for (var i = _cookieName.Length + 1; i < name.Length; i++)
            {
                if (!char.IsDigit(name[i]))
                    return false;
            }
            return true;
        }

        return false;
    }

    private static string? TryGetHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;
    }
}
