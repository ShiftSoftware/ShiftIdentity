using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Model;
using ShiftSoftware.TypeAuth.Blazor.Services;
using System.Net;
using System.Net.Http.Json;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public class HttpMessageHandlerService
{
    private readonly TypeAuthService? typeAuth;
    private readonly AuthenticationStateProvider? authStateProvider;
    private readonly NavigationManager navManager;
    private readonly ShiftIdentityBlazorOptions options;
    private readonly IIdentityTokenProvider tokenProvider;
    private readonly IIdentityTokenStore tokenStore;
    private HttpClient http;

    public HttpMessageHandlerService(
        NavigationManager navManager,
        ShiftIdentityBlazorOptions options,
        IIdentityTokenProvider tokenProvider,
        IIdentityTokenStore tokenStore,
        TypeAuthService? typeAuth = null,
        AuthenticationStateProvider? authStateProvider = null)
    {
        this.typeAuth = typeAuth;
        this.authStateProvider = authStateProvider;
        this.navManager = navManager;
        this.options = options;
        this.tokenProvider = tokenProvider;
        this.tokenStore = tokenStore;
        http = new HttpClient() { BaseAddress = new Uri(options.BaseUrl) };
    }

    public async Task RefreshAsync()
    {
        var storedToken = await tokenProvider.GetTokenAsync();

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
    }

    private async Task NofityChanges()
    {
        //Notify AuthenticationStateProvider that state has changed
        if (authStateProvider is not null)
            await authStateProvider.GetAuthenticationStateAsync();

        //Notify TypeAuth that state has changed
        if (typeAuth is not null)
            typeAuth.AuthStateHasChanged();
    }
}
