using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Model;
using ShiftSoftware.TypeAuth.Blazor.Services;
using System.Net;
using System.Net.Http.Headers;

namespace ShiftSoftware.ShiftIdentity.Blazor.Extensions;

public static class WebAssemblyHostExtensions
{
    public static async Task<WebAssemblyHost> RefreshTokenAsync(this WebAssemblyHost host, int everySeconds)
    {
        //Get injected services
        var shiftIdentityProvider = host.Services.GetRequiredService<ShiftIdentityProvider>();
        var tokenProvider = host.Services.GetRequiredService<IIdentityTokenProvider>();
        var navManager = host.Services.GetRequiredService<NavigationManager>();
        var http = host.Services.GetRequiredService<HttpClient>();
        var storeToken = host.Services.GetRequiredService<IIdentityTokenStore>();
        var shiftIdentityService = host.Services.GetRequiredService<ShiftIdentityService>();
        var options = host.Services.GetRequiredService<ShiftIdentityBlazorOptions>();
        var typeAuth = host.Services.GetService<TypeAuthService>();
        var authStateProvider = host.Services.GetService<AuthenticationStateProvider>();

        await RefreshAsync(shiftIdentityProvider, tokenProvider, typeAuth, authStateProvider, navManager, http, storeToken, 
            shiftIdentityService, options);

        var timer = new System.Timers.Timer(TimeSpan.FromSeconds(everySeconds));
        timer.Enabled = true;
        timer.Elapsed += async (s, e) =>
            await RefreshAsync(shiftIdentityProvider, tokenProvider, typeAuth, authStateProvider, navManager, http, storeToken, 
            shiftIdentityService, options);

        return host;
    }

    private static async Task RefreshAsync(
        ShiftIdentityProvider shiftIdentityProvider,
        IIdentityTokenProvider tokenProvider,
        TypeAuthService? typeAuth,
        AuthenticationStateProvider? authStateProvider,
        NavigationManager navManager,
        HttpClient http,
        IIdentityTokenStore storeToken,
        ShiftIdentityService shiftIdentityService,
        ShiftIdentityBlazorOptions options)
    {

        var storedToken = await tokenProvider.GetTokenAsync();

        var headerToken = http.DefaultRequestHeaders?.Authorization?.Parameter;

        if (headerToken is not null && headerToken != storedToken.Token)
        {
            //Set authorize header of http-client for prevent refresh on multiple tabs or windows
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", storedToken?.Token);

            await NofityChanges(typeAuth, authStateProvider);
            return;
        }

        var refreshToken = storedToken?.RefreshToken;

        try
        {
            var result = await shiftIdentityProvider.RefreshTokenAsync(options.BaseUrl, new RefreshDTO { RefreshToken = refreshToken });

            if (result.IsSuccess)
            {
                //Store new token
                await storeToken.StoreTokenAsync(result?.Data?.Entity!);

                //Set authorize header of http-client for prevent refresh on multiple tabs or windows
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result?.Data?.Entity?.Token);

                await NofityChanges(typeAuth, authStateProvider);
            }
            else if (result.StatusCode == HttpStatusCode.Unauthorized)
            {
                shiftIdentityService.LoginAsync();
            }
        }
        catch (Exception){}
    }

    private static async Task NofityChanges(
        TypeAuthService? typeAuth,
        AuthenticationStateProvider? authStateProvider)
    {
        //Notify AuthenticationStateProvider that state has changed
        if (authStateProvider is not null)
            await authStateProvider.GetAuthenticationStateAsync();

        //Notify TypeAuth that state has changed
        if (typeAuth is not null)
            typeAuth.AuthStateHasChanged();
    }
}
