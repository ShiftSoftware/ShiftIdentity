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

        public async Task<HttpResponse<ShiftEntityResponse<AuthCodeModel>>> GenerateAuthCodeAsync(GenerateAuthCodeDTO dto)
        {
            return await httpService.PostAsync<ShiftEntityResponse<AuthCodeModel>, GenerateAuthCodeDTO>(url + "AuthCode", dto);
        }

        public async Task LogOutAsync()
        {
            await storageService.RemoveTokenAsync();
        }
    }
}
