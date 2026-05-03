using Microsoft.JSInterop;
using ShiftSoftware.ShiftIdentity.Blazor.Providers;
using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

/// <summary>
/// Unified WASM auth refresh loop for JWT (standalone) and cookie (Blazor Web App). Pluggable
/// per-tick behavior is provided by <see cref="IAuthRefreshStrategy"/>; this service owns the
/// timer, visibility skip, in-memory provider mutation, and login/logout entry points.
///
/// Each tick:
/// <list type="number">
///   <item>Skip if <c>document.visibilityState != "visible"</c> (saves traffic for backgrounded tabs).</item>
///   <item>Call <see cref="IAuthRefreshStrategy.RefreshAsync"/>.</item>
///   <item>On non-null claims → push to <see cref="ShiftAuthStateProvider"/>. On null → log out. On exception → keep state, retry next tick.</item>
/// </list>
/// </summary>
public class AuthSessionService : IAsyncDisposable
{
    private readonly IAuthRefreshStrategy _strategy;
    private readonly ShiftAuthStateProvider _provider;
    private readonly IJSRuntime _js;

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private TimeSpan _interval = TimeSpan.FromSeconds(60);
    private bool _started;

    public AuthSessionService(
        IAuthRefreshStrategy strategy,
        ShiftAuthStateProvider provider,
        IJSRuntime js)
    {
        _strategy = strategy;
        _provider = provider;
        _js = js;
    }

    /// <summary>
    /// Seed the provider from synchronously available state and start the polling loop.
    /// Idempotent — safe to call multiple times. <paramref name="intervalSeconds"/> is the
    /// fixed cadence between refresh ticks; ticks are skipped when the tab is hidden.
    /// </summary>
    public async Task StartAsync(int intervalSeconds = 60)
    {
        if (_started) return;
        _started = true;
        _interval = TimeSpan.FromSeconds(Math.Max(1, intervalSeconds));

        var initial = _strategy.GetInitialClaims();
        if (initial is { Count: > 0 })
            _provider.NotifyUserAuthentication(initial);

        // First-tick refresh validates the initial claims against the server and clears
        // them if the stored token / cookie is stale.
        await TickAsync(skipVisibilityCheck: true);

        StartLoop();
    }

    /// <summary>
    /// Apply login claims to the provider and ensure the loop is running. Call from the
    /// login UI after a successful login response.
    /// </summary>
    public async Task OnLoginSuccessAsync(AuthLoginResult result)
    {
        var claims = await _strategy.OnLoginCommittedAsync(result);
        if (claims is { Count: > 0 })
            _provider.NotifyUserAuthentication(claims);

        if (!_started)
        {
            _started = true;
            StartLoop();
        }
    }

    /// <summary>
    /// Stop the loop, run logout side effects, and clear the provider. Call from the
    /// logout UI or from <see cref="Handlers.Auth401Handler"/> on a 401.
    /// </summary>
    public async Task OnLogoutAsync()
    {
        StopLoop();
        try { await _strategy.OnLogoutAsync(); } catch { /* best-effort */ }
        _provider.NotifyUserLoggedOut();
    }

    private void StartLoop()
    {
        StopLoop();
        var cts = new CancellationTokenSource();
        _loopCts = cts;
        _loopTask = RunLoopAsync(cts.Token);
    }

    private void StopLoop()
    {
        var cts = _loopCts;
        _loopCts = null;
        try { cts?.Cancel(); } catch { }
        cts?.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(_interval, ct);
                await TickAsync(skipVisibilityCheck: false);
            }
        }
        catch (OperationCanceledException) { /* expected on stop / dispose */ }
    }

    private async Task TickAsync(bool skipVisibilityCheck)
    {
        if (!skipVisibilityCheck && !await IsTabVisibleAsync())
            return;

        try
        {
            var claims = await _strategy.RefreshAsync();
            if (claims is { Count: > 0 })
                _provider.NotifyUserAuthentication(claims);
            else if (claims is null)
                await OnLogoutAsync();
        }
        catch
        {
            // transient (network, 5xx) — keep current state, retry next tick
        }
    }

    private async Task<bool> IsTabVisibleAsync()
    {
        try
        {
            var visibility = await _js.GetValueAsync<string>("document.visibilityState");
            return visibility == "visible";
        }
        catch
        {
            // SSR / no JS interop — assume visible so the loop still runs
            return true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        StopLoop();
        if (_loopTask != null)
        {
            try { await _loopTask; } catch { }
        }
    }
}
