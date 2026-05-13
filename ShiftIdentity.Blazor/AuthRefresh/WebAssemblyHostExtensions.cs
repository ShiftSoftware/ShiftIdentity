using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ShiftSoftware.ShiftIdentity.Blazor.AuthRefresh;

public static class WebAssemblyHostExtensions
{
    /// <summary>
    /// Starts the unified <see cref="AuthSessionService"/> loop. Call once at app startup
    /// (after <c>builder.Build()</c>, before <c>host.RunAsync()</c>) for both standalone-WASM
    /// (JWT) and Blazor-Web-App-client (cookie) apps. The loop polls every
    /// <paramref name="everySeconds"/> while the tab is visible.
    /// </summary>
    public static async Task<WebAssemblyHost> RefreshTokenAsync(this WebAssemblyHost host, int everySeconds)
    {
        var refreshService = host.Services.GetRequiredService<AuthSessionService>();
        await refreshService.StartAsync(everySeconds);
        return host;
    }
}
