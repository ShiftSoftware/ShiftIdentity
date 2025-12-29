using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using System.Net.Http.Json;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public class HttpMessageHandlerService
{
    private readonly AuthenticationStateProvider? authStateProvider;
    private readonly NavigationManager navManager;
    private readonly ShiftIdentityBlazorOptions options;
    private readonly IIdentityStore tokenStore;
    private readonly MessageService messageService;
    private readonly TokenRefreshService tokenRefreshService;

    public HttpMessageHandlerService(
        NavigationManager navManager,
        ShiftIdentityBlazorOptions options,
        IIdentityStore tokenStore,
        MessageService messageService,
        TokenRefreshService tokenRefreshService,
        AuthenticationStateProvider? authStateProvider = null)
    {
        this.authStateProvider = authStateProvider;
        this.navManager = navManager;
        this.options = options;
        this.tokenStore = tokenStore;
        this.messageService = messageService;
        this.tokenRefreshService = tokenRefreshService;
    }

    public async Task<bool?> RefreshAsync()
    {
        var storedToken = await tokenStore.GetTokenAsync();

        if (storedToken is null)
        {
            await tokenStore.RemoveTokenAsync();
            await messageService.ShowWarningMessageAsync("Your session has expired. Please login again (in another tab). ", "Login another tab");
            return false;
        }

        var refreshToken = storedToken.RefreshToken;

        var result = await tokenRefreshService.RefreshTokenAsync(refreshToken);

        if (result is not null)
        {
            //Store new token
            await tokenStore.StoreTokenAsync(result);

            //Remove warning message if exists
            await messageService.RemoveWarningMessageAsync();

            await NofityChanges();
            return true;
        }
        else
        {
            await tokenStore.RemoveTokenAsync();
            await messageService.ShowWarningMessageAsync("Your session has expired. Please login again (in another tab). ", "Login another tab");
            return false;
        }
    }

    private async Task NofityChanges()
    {
        //Notify AuthenticationStateProvider that state has changed
        if (authStateProvider is not null)
            await authStateProvider.GetAuthenticationStateAsync();
    }
}
