using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Blazor.AuthRefresh;

/// <summary>
/// WASM <see cref="HttpClient"/> handler that watches for 401 on authenticated calls and drives
/// the user back to anonymous via <see cref="AuthSessionService.OnLogoutAsync"/>. Covers both JWT
/// and cookie auth (the cookie path's <c>NoOpIdentityStore</c> makes the token-store clear a no-op).
/// Skipped for unauthenticated endpoints (login, sign-in-with-token, logout) so a failed login
/// doesn't trigger a logout cycle. Resolves <see cref="AuthSessionService"/> lazily via
/// <see cref="IServiceProvider"/> — a constructor dependency would form a DI cycle with the
/// <see cref="HttpClient"/> the refresh service uses.
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

    public Auth401Handler(IServiceProvider services)
    {
        _services = services;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && !ShouldSkip(request))
        {
            var refreshService = _services.GetService<AuthSessionService>();
            if (refreshService != null)
                _ = refreshService.OnLogoutAsync();

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
