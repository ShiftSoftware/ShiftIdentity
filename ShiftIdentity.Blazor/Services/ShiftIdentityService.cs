using Microsoft.AspNetCore.Components;
using ShiftSoftware.ShiftIdentity.Blazor.Providers;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public class ShiftIdentityService
{
    private readonly ShiftIdentityBlazorOptions options;
    private readonly NavigationManager navManager;
    private readonly CodeVerifierService codeVerifierService;
    private readonly IIdentityStore tokenStore;
    private readonly IShiftIdentityProvider shiftIdentityProvider;
    private readonly CodeVerifierStorageService codeVerifierStorage;

    public ShiftIdentityService(ShiftIdentityBlazorOptions options, 
        NavigationManager navManager,
        CodeVerifierService codeVerifierService,
        IIdentityStore tokenStore,
        IShiftIdentityProvider shiftIdentityProvider,
        CodeVerifierStorageService codeVerifierStorage)
    {
        this.options = options;
        this.navManager = navManager;
        this.codeVerifierService = codeVerifierService;
        this.tokenStore = tokenStore;
        this.shiftIdentityProvider = shiftIdentityProvider;
        this.codeVerifierStorage = codeVerifierStorage;
    }

    public async Task LoginAsync(string returnUrl="")
    {
        var codeChallenge = await codeVerifierService.GenerateCodeChallengeAsync();

        var queryStrings = new Dictionary<string, object?>();
        queryStrings.Add("AppId", options.AppId);
        queryStrings.Add("CodeChallenge", codeChallenge);

        //Add return-url to login page
        if (!string.IsNullOrWhiteSpace(returnUrl))
            queryStrings.Add("ReturnUrl", returnUrl);
        var url = $"{(options.FrontEndBaseUrl.EndsWith('/') ? options.FrontEndBaseUrl : options.FrontEndBaseUrl + "/")}{Core.Constants.IdentityRoutePreifix}/Auth/AuthCode";
        var uri = navManager.GetUriWithQueryParameters(url, queryStrings);
        navManager.NavigateTo(uri, true);
    }

    internal async Task GetTokenAsync(Guid authCode, string returnUrl)
    {
        var codeVerifier = await codeVerifierService.LoadCodeVerifierAsync();

        var response = await shiftIdentityProvider.GetTokenWithAppIdOnlyAsync(options.BaseUrl,
            new GenerateExternalTokenWithAppIdOnlyDTO
            {
                AppId = options.AppId,
                AuthCode = authCode,
                CodeVerifier = codeVerifier
            });

        if(response.IsSuccess)
        {
            await tokenStore.StoreTokenAsync(response?.Data.Entity!);
            await codeVerifierStorage.RemoveCodeVerifierAsync();
            navManager.NavigateTo("/" + returnUrl, true);
        }
        else
        {
            navManager.NavigateTo("/");
        }
    }
}
