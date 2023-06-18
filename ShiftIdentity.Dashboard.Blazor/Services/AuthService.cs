using Microsoft.AspNetCore.Components;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Auth;
using System.Net.Http.Headers;
using System.Net;
using Microsoft.AspNetCore.Components.Authorization;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Models;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services
{
    public class AuthService
    {
        private HttpService httpService;
        private readonly StorageService storageService;
        private readonly AuthenticationStateProvider? authStateProvider;
        private readonly HttpClient http;
        private readonly NavigationManager navManager;
        private const string url = "api/auth/";

        public AuthService(
            HttpService httpService,
            StorageService storageService,
            
            AuthenticationStateProvider? authStateProvider,
            NavigationManager navManager,
            HttpClient http)
        {
            this.httpService = httpService;
            this.storageService = storageService;
            this.authStateProvider = authStateProvider;
            this.navManager = navManager;
            this.http = http;
        }

        public async Task<HttpResponse<ShiftEntityResponse<TokenDTO>>> LoginAsync(LoginDTO loginDto)
        {
            return await httpService.PostAsync<ShiftEntityResponse<TokenDTO>, LoginDTO>(url + "login", loginDto);
        }

        //public async Task<HttpResponse<ShiftEntityResponse<TokenDTO>>> RefreshAsync(RefreshDTO refreshDto)
        //{
        //    return await httpService.PostAsync<ShiftEntityResponse<TokenDTO>, RefreshDTO>(url + "Refresh", refreshDto);
        //}

        //public async Task RefreshAsync()
        //{
        //    var storedToken = await storageService.GetTokenAsync();

        //    var headerToken = http.DefaultRequestHeaders?.Authorization?.Parameter;

        //    if (headerToken is not null && headerToken != storedToken.Token)
        //    {
        //        //Set authorize header of http-client for prevent refresh on multiple tabs or windows
        //        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", storedToken?.Token);

        //        await NofityChanges();
        //        return;
        //    }

        //    var refreshToken = storedToken?.RefreshToken;



        //    var result = await RefreshAsync(new RefreshDTO { RefreshToken = refreshToken });

        //    if (result.IsSuccess)
        //    {
        //        //Store new token
        //        await storageService.StoreTokenAsync(result?.Data?.Entity!);

        //        //Set authorize header of http-client for prevent refresh on multiple tabs or windows
        //        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result?.Data?.Entity?.Token!);

        //        await NofityChanges();
        //    }
        //    else if (result.StatusCode == HttpStatusCode.Unauthorized || result.StatusCode == HttpStatusCode.Forbidden)
        //    {
        //        await storageService.RemoveTokenAsync();

        //        navManager.NavigateTo("/", true);
        //    }
        //}

        //private async Task NofityChanges()
        //{
        //    //Notify AuthenticationStateProvider that state has changed
        //    if (authStateProvider is not null)
        //        await authStateProvider.GetAuthenticationStateAsync();

        //    ////Notify TypeAuth that state has changed
        //    //if (typeAuth is not null)
        //    //    typeAuth.AuthStateHasChanged();
        //}

        public async Task<HttpResponse<ShiftEntityResponse<AuthCodeModel>>> GenerateAuthCodeAsync(GenerateAuthCodeDTO dto)
        {
            return await httpService.PostAsync<ShiftEntityResponse<AuthCodeModel>, GenerateAuthCodeDTO>(url + "AuthCode", dto);
        }

        //public async Task LogOutAsync()
        //{
        //    await storageService.RemoveTokenAsync();
        //}
    }
}
