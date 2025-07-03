using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using ShiftSoftware.ShiftIdentity.Blazor.Providers;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using System.Net.Http.Headers;

namespace ShiftSoftware.ShiftIdentity.Blazor.Extensions;

public static class WebAssemblyHostExtensions
{
    public static async Task<WebAssemblyHost> RefreshTokenAsync(this WebAssemblyHost host, int everySeconds)
    {
        await RefreshAsync(host.Services, true);

        var timer = new System.Timers.Timer(TimeSpan.FromSeconds(everySeconds));
        timer.Enabled = true;
        timer.Elapsed += async (s, e) =>
            await RefreshAsync(host.Services, false);

        return host;
    }

    private static async Task RefreshAsync(
        IServiceProvider services,
        bool firtTimeRun = false)
    {
        //Get injected services
        var shiftIdentityProvider = services.GetRequiredService<IShiftIdentityProvider>();
        var http = services.GetRequiredService<HttpClient>();
        var tokenStore = services.GetRequiredService<IIdentityStore>();
        var options = services.GetRequiredService<ShiftIdentityBlazorOptions>();
        var authStateProvider = services.GetService<AuthenticationStateProvider>();
        var messageService = services.GetRequiredService<MessageService>();

        //Skip the refresh if tab is not active
        var isTabActive = await IsTabActive(services);
        if (!isTabActive && !firtTimeRun)
            return;

        var storedToken = await tokenStore.GetTokenAsync();

        var headerToken = http.DefaultRequestHeaders?.Authorization?.Parameter;

        if (headerToken is not null && headerToken != storedToken?.Token)
        {
            //Set authorize header of http-client for prevent refresh on multiple tabs or windows
            http.DefaultRequestHeaders!.Authorization = new AuthenticationHeaderValue("Bearer", storedToken?.Token);

            if(!string.IsNullOrWhiteSpace(storedToken?.Token))
                await NofityChanges(authStateProvider);

            return;
        }

        if (storedToken is null)
            return;

        var refreshToken = storedToken?.RefreshToken;

        try
        {
            var result = await shiftIdentityProvider.RefreshTokenAsync(options.BaseUrl, new RefreshDTO { RefreshToken = refreshToken });

            if (result.IsSuccess)
            {
                //Store new token
                await tokenStore.StoreTokenAsync(result?.Data?.Entity!);

                //Remove warning message if exists
                await messageService.RemoveWarningMessageAsync();

                //Set authorize header of http-client for prevent refresh on multiple tabs or windows
                http.DefaultRequestHeaders!.Authorization = new AuthenticationHeaderValue("Bearer", result?.Data?.Entity?.Token);

                await NofityChanges(authStateProvider);
            }
            else
            {
                if (firtTimeRun)
                    await tokenStore.RemoveTokenAsync();
                //if (!firtTimeRun)
                //    await messageService.ShowWarningMessageAsync("Your session has expired. Please login again in another tab or refresh.");
            }
        }
        catch (Exception){}
    }

    private static async Task NofityChanges(AuthenticationStateProvider? authStateProvider)
    {
        //Notify AuthenticationStateProvider that state has changed
        if (authStateProvider is not null)
            await authStateProvider.GetAuthenticationStateAsync();
    }

    private static async Task<bool> IsTabActive(IServiceProvider services)
    {
        var js = services.GetRequiredService<IJSRuntime>();

        var visiblity = await js.InvokeAsync<string>("eval", "document.visibilityState");
        return visiblity.ToLower() == "visible";
    }
}
