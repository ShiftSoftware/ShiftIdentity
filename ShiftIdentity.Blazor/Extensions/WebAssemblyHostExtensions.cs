using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.Blazor.Providers;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;

namespace ShiftSoftware.ShiftIdentity.Blazor.Extensions;

public static class WebAssemblyHostExtensions
{
    public static async Task<WebAssemblyHost> RefreshTokenAsync(this WebAssemblyHost host, int everySeconds)
    {
        //Get injected services
        var shiftIdentityProvider = host.Services.GetRequiredService<IShiftIdentityProvider>();
        var navManager = host.Services.GetRequiredService<NavigationManager>();
        var http = host.Services.GetRequiredService<HttpClient>();
        var tokenStore = host.Services.GetRequiredService<IIdentityStore>();
        var shiftIdentityService = host.Services.GetRequiredService<ShiftIdentityService>();
        var options = host.Services.GetRequiredService<ShiftIdentityBlazorOptions>();
        var authStateProvider = host.Services.GetService<AuthenticationStateProvider>();

        await RefreshAsync(shiftIdentityProvider, authStateProvider, navManager, http, tokenStore, 
            shiftIdentityService, options);

        var timer = new System.Timers.Timer(TimeSpan.FromSeconds(everySeconds));
        timer.Enabled = true;
        timer.Elapsed += async (s, e) =>
            await RefreshAsync(shiftIdentityProvider, authStateProvider, navManager, http, tokenStore, 
            shiftIdentityService, options);

        return host;
    }

    private static async Task RefreshAsync(
        IShiftIdentityProvider shiftIdentityProvider,
        AuthenticationStateProvider? authStateProvider,
        NavigationManager navManager,
        HttpClient http,
        IIdentityStore tokenStore,
        ShiftIdentityService shiftIdentityService,
        ShiftIdentityBlazorOptions options)
    {

        var storedToken = await tokenStore.GetTokenAsync();

        var headerToken = http.DefaultRequestHeaders?.Authorization?.Parameter;

        if (headerToken is not null && headerToken != storedToken.Token)
        {
            //Set authorize header of http-client for prevent refresh on multiple tabs or windows
            http.DefaultRequestHeaders!.Authorization = new AuthenticationHeaderValue("Bearer", storedToken?.Token);

            await NofityChanges(authStateProvider);
            return;
        }

        var refreshToken = storedToken?.RefreshToken;

        try
        {
            var result = await shiftIdentityProvider.RefreshTokenAsync(options.BaseUrl, new RefreshDTO { RefreshToken = refreshToken });

            if (result.IsSuccess)
            {
                //Store new token
                await tokenStore.StoreTokenAsync(result?.Data?.Entity!);

                //Set authorize header of http-client for prevent refresh on multiple tabs or windows
                http.DefaultRequestHeaders!.Authorization = new AuthenticationHeaderValue("Bearer", result?.Data?.Entity?.Token);

                await NofityChanges(authStateProvider);
            }
            else if (result.StatusCode == HttpStatusCode.Unauthorized)
            {
                await tokenStore.RemoveTokenAsync();
                navManager.NavigateTo(navManager.Uri, true);
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
}
