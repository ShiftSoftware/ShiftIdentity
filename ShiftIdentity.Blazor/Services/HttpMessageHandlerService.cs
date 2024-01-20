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
    private HttpClient http;

    public HttpMessageHandlerService(
        NavigationManager navManager,
        ShiftIdentityBlazorOptions options,
        IIdentityStore tokenStore,
        MessageService messageService,
        AuthenticationStateProvider? authStateProvider = null)
    {
        this.authStateProvider = authStateProvider;
        this.navManager = navManager;
        this.options = options;
        this.tokenStore = tokenStore;
        this.messageService = messageService;
        http = new HttpClient() { BaseAddress = new Uri(options.BaseUrl) };
    }

    public async Task RefreshAsync()
    {
        var storedToken = await tokenStore.GetTokenAsync();

        var headerToken = http.DefaultRequestHeaders?.Authorization?.Parameter;

        if (headerToken is not null && headerToken != storedToken.Token)
        {
            await NofityChanges();
            return;
        }

        var refreshToken = storedToken?.RefreshToken;

        using var response =await http.PostAsJsonAsync<RefreshDTO>("api/auth/" + "Refresh", new RefreshDTO { RefreshToken = refreshToken });

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>();

            //Store new token
            await tokenStore.StoreTokenAsync(result?.Entity!);

            await NofityChanges();
        }
        else
        {
            await tokenStore.RemoveTokenAsync();
            await messageService.ShowWarningMessageAsync("Your session has expired. Please login again in another tab or refresh.");
        }
    }

    private async Task NofityChanges()
    {
        //Notify AuthenticationStateProvider that state has changed
        if (authStateProvider is not null)
            await authStateProvider.GetAuthenticationStateAsync();
    }
}
