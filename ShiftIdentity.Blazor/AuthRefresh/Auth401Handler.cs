using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Blazor.AuthRefresh;

/// <summary>
/// WASM <see cref="HttpClient"/> handler that watches for 401 on authenticated calls and
/// drives the user back to anonymous via <see cref="AuthSessionService.OnLogoutAsync"/>.
/// Works for both JWT (refresh-token expired / revoked) and cookie (cookie expired / revoked)
/// auth — the cookie path's <c>NoOpIdentityStore</c> makes the token-store clear a no-op,
/// so the same handler covers both modes.
///
/// Skipped for unauthenticated endpoints (login, sign-in-with-token, logout) so a failed
/// login attempt does not trigger a logout cycle.
///
/// Resolves <see cref="AuthSessionService"/> lazily through <see cref="IServiceProvider"/>:
/// taking it as a constructor parameter would form a DI cycle with the
/// <see cref="HttpClient"/> the refresh service itself uses.
/// </summary>
public class Auth401Handler : DelegatingHandler
{
    private static readonly string[] SkipPaths =
    {
        "/api/identity/login",
        "/api/identity/sign-in-with-token",
        "/api/identity/logout",
        "/Auth/Login",
        "/Auth/Refresh",
    };

    private readonly IServiceProvider _services;
    private readonly AuthSessionService _refreshService;

    public Auth401Handler(IServiceProvider services, AuthSessionService refreshService)
    {
        _services = services;
        _refreshService = refreshService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && !ShouldSkip(request))
        {
            _ = _refreshService.OnLogoutAsync();

            // JWT: clear the stale token so a reload doesn't transiently rehydrate from localStorage.
            // Cookie: NoOpIdentityStore makes this a no-op.
            var tokenStore = _services.GetService<IIdentityStore>();
            if (tokenStore != null)
                _ = tokenStore.RemoveTokenAsync();
        }

        return response;
    }

    private static bool ShouldSkip(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath;
        if (path == null) return false;
        foreach (var skip in SkipPaths)
        {
            if (path.EndsWith(skip, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
