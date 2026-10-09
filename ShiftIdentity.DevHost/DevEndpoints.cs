using System.Net;
using Microsoft.EntityFrameworkCore;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;

namespace ShiftIdentity.DevHost;

/// <summary>What the /dev tools read: the fixture, the seeded accounts and the addresses this run serves.</summary>
internal sealed record DevHostState(SqlIdentityFixture Fixture, IReadOnlyList<DevAccount> Accounts,
    IReadOnlyList<string> Origins, string PublicOrigin)
{
    /// <summary>The fault the refresh route simulates: none, offline (the connection drops) or server-error (a bare 500).</summary>
    public string RefreshFault { get; set; } = "none";
}

internal static class DevEndpoints
{
    /// <summary>
    /// Refuses every request that is not for this run: from an address other than loopback (or a private LAN address
    /// when the host binds one), for another host name, or a state-changing request from another origin. A web page
    /// on any other site therefore cannot use the /dev shortcuts. Adds the response headers of a local tool.
    /// A request through the tunnel reaches the Identity app and its API, not the /dev tools. The tunnel's relay
    /// connects from loopback and names the tunnel's host in X-Forwarded-Host (it rewrites Host to localhost), or
    /// leaves it in Host when the tunnel keeps the header. A page request for /dev through the tunnel is sent to the
    /// loopback address instead, so that the browser Visual Studio opens on the tunnel address lands on the tools.
    /// </summary>
    public static void UseDevHostGuard(this WebApplication app, IEnumerable<string> hosts, bool lan, string loopbackOrigin, string? tunnelOrigin)
    {
        var direct = hosts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tunnelHost = tunnelOrigin is null ? null : new Uri(tunnelOrigin).Authority;
        var tunnelSeen = 0;
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            var remote = context.Connection.RemoteIpAddress;
            var loopback = remote is not null && IPAddress.IsLoopback(remote);
            var reading = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method);
            var origin = request.Headers.Origin.ToString();
            var forwardedHost = request.Headers["X-Forwarded-Host"].ToString().Split(',')[0].Trim();
            var tunnelled = loopback && tunnelHost is not null &&
                (string.Equals(request.Host.Value, tunnelHost, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(forwardedHost, tunnelHost, StringComparison.OrdinalIgnoreCase));
            if (tunnelled)
            {
                if (Interlocked.Exchange(ref tunnelSeen, 1) == 0)
                    app.Logger.LogInformation("The first request through the tunnel arrived. The /dev tools stay on {Loopback}.", loopbackOrigin);
                if (request.Path.StartsWithSegments("/dev"))
                {
                    if (reading) context.Response.Redirect(loopbackOrigin + request.Path + request.QueryString);
                    else context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }
            else if (remote is null || !(loopback || (lan && DevHostOptions.IsPrivate(remote))) || !direct.Contains(request.Host.Value ?? ""))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            // A state-changing request comes from this run's own pages: the tunnel's origin when the relay rewrites
            // only Host, and the request's own when the request is direct or the relay rewrites Origin as well.
            if (!reading && origin != $"{request.Scheme}://{request.Host}" && !(loopback && tunnelOrigin is not null && origin == tunnelOrigin))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval' 'unsafe-eval'; " +
                "style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'self'; " +
                "frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
            await next(context);
        });
    }

    public static void MapDevEndpoints(this WebApplication app)
    {
        // One route covers /dev and /dev/: routing ignores a trailing slash, so a second route for /dev/ made both
        // addresses ambiguous (500).
        app.MapGet("/dev", () => Results.Redirect("/dev/index.html"));
        app.MapGet("/dev/info", async (DevHostState state, CancellationToken cancellation) =>
        {
            var now = state.Fixture.Clock.GetUtcNow();
            // Read live: the admin shortcuts below change it.
            var ids = state.Accounts.Select(x => x.UserID).ToArray();
            Dictionary<long, bool> allowed;
            await using (var db = state.Fixture.CreateContext())
                allowed = await db.Users.IgnoreQueryFilters().Where(x => ids.Contains(x.ID)).ToDictionaryAsync(x => x.ID, x => x.AllowDeviceSignIn, cancellation);
            var accounts = new List<object>();
            foreach (var account in state.Accounts)
            {
                var code = account.Mfa ? (await state.Fixture.GetSyntheticFactorAsync(account.Username, now, cancellation)).Code : null;
                accounts.Add(new { username = account.Username, description = account.Description, userID = account.UserID, code,
                    deviceSignIn = allowed.GetValueOrDefault(account.UserID) });
            }
            return Results.Ok(new
            {
                password = state.Fixture.Password,
                codeExpiresAt = DateTimeOffset.FromUnixTimeSeconds((now.ToUnixTimeSeconds() / 30 + 1) * 30),
                publicOrigin = state.PublicOrigin,
                origins = state.Origins,
                accounts
            });
        });
        app.MapPost("/dev/stop", (IHostApplicationLifetime lifetime) => { lifetime.StopApplication(); return Results.Ok(); });

        // Shortcuts that change an account directly in the owned database, as an administrator would through the
        // account screens, so that a device session's next refresh can be seen to fail.
        app.MapPost("/dev/admin/users/{id:long}/{change:regex(^(deactivate|activate|bump-security-version|disallow-device-sign-in)$)}",
            async (long id, string change, DevHostState state, CancellationToken cancellation) =>
        {
            await using var db = state.Fixture.CreateContext();
            var user = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ID == id, cancellation);
            var security = await db.Set<UserSecurityState>().SingleOrDefaultAsync(x => x.UserID == id, cancellation);
            if (user is null || security is null) return Results.NotFound();
            switch (change)
            {
                case "deactivate": user.IsActive = false; break;
                case "activate": user.IsActive = true; break;
                // As the user form saves it: taking device sign-in away also ends the account's sessions.
                case "disallow-device-sign-in": user.AllowDeviceSignIn = false; security.SecurityVersion++; break;
                default: security.SecurityVersion++; break;
            }
            await db.SaveChangesAsync(cancellation);
            return Results.Ok(new { userID = id, username = user.Username, isActive = user.IsActive, allowDeviceSignIn = user.AllowDeviceSignIn,
                securityVersion = security.SecurityVersion });
        });

        // Ends every unfinished device sign-in now, so that "let the code expire" needs no ten-minute wait.
        app.MapPost("/dev/admin/device-authorizations/expire", async (DevHostState state, CancellationToken cancellation) =>
        {
            var now = state.Fixture.Clock.GetUtcNow();
            await using var db = state.Fixture.CreateContext();
            var expired = await db.Set<DeviceAuthorization>()
                .Where(x => x.State == DeviceAuthorizationState.Pending || x.State == DeviceAuthorizationState.Approved)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.CreatedAt, now.AddMinutes(-11)).SetProperty(y => y.ExpiresAt, now.AddSeconds(-1)), cancellation);
            return Results.Ok(new { expired });
        });

        app.MapGet("/dev/faults", (DevHostState state) => Results.Ok(new { refresh = state.RefreshFault }));
        app.MapPost("/dev/faults/refresh/{mode:regex(^(none|offline|server-error)$)}", (string mode, DevHostState state) =>
        {
            state.RefreshFault = mode;
            return Results.Ok(new { refresh = mode });
        });
    }

    /// <summary>Simulates the refresh fault the /dev tools selected. Only the refresh route is affected.</summary>
    public static void UseRefreshFaults(this WebApplication app) => app.Use(async (context, next) =>
    {
        if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path.Equals("/api/identity/v2/refresh", StringComparison.OrdinalIgnoreCase))
        {
            var fault = context.RequestServices.GetRequiredService<DevHostState>().RefreshFault;
            if (fault == "offline") { context.Abort(); return; }
            if (fault == "server-error") { context.Response.StatusCode = StatusCodes.Status500InternalServerError; return; }
        }
        await next(context);
    });
}
