using Microsoft.AspNetCore.Components;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Auth;
using System.Net.Http.Headers;
using System.Net;
using Microsoft.AspNetCore.Components.Authorization;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Blazor;
using System.Text.Json;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services
{
    public class AuthService
    {
        private HttpService httpService;
        private readonly IdentitySession storageService;
        private readonly AuthenticationStateProvider? authStateProvider;
        private readonly HttpClient http;
        private readonly NavigationManager navManager;
        private readonly ShiftIdentityDashboardBlazorOptions? options;
        private const string url = "auth/";

        public AuthService(
            HttpService httpService,
            IdentitySession storageService,
            
            AuthenticationStateProvider? authStateProvider,
            NavigationManager navManager,
            HttpClient http,
            ShiftIdentityDashboardBlazorOptions? options = null)
        {
            this.options = options;
            this.httpService = httpService;
            this.storageService = storageService;
            this.authStateProvider = authStateProvider;
            this.navManager = navManager;
            this.http = http;
        }

        public async Task<HttpResponse<ShiftEntityResponse<TokenDTO>>> LoginAsync(LoginDTO loginDto)
        {
            return await httpService.PostAsync<ShiftEntityResponse<TokenDTO>, LoginDTO>(url + "login",
                new LoginDTO { Username = loginDto.Username?.Trim()!, Password = loginDto.Password });
        }

        public async Task<HttpResponse<ShiftEntityResponse<TokenDTO>>> VerifyMfaAsync(MfaDTO mfaDto)
        {
            return await httpService.PostAsync<ShiftEntityResponse<TokenDTO>, MfaDTO>(url + "login/mfa", mfaDto);
        }

        public async Task<HttpResponse<ShiftEntityResponse<AuthCodeModel>>> GenerateAuthCodeAsync(GenerateAuthCodeDTO dto)
        {
            if (options?.StagedAuthority == true)
            {
                var current = await storageService.GetTokenAsync();
                if (current is { Flow: ShiftSoftware.ShiftIdentity.Core.Enums.AuthPurpose.None } &&
                    !string.IsNullOrWhiteSpace(current.RefreshToken) && !IsAuthorityToken(current.Token))
                    await storageService.RenewAsync();
            }
            return await httpService.PostAsync<ShiftEntityResponse<AuthCodeModel>, GenerateAuthCodeDTO>(url + "AuthCode", dto);
        }

        private static bool IsAuthorityToken(string? token)
        {
            try { return new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(token).GetClaim("shift_schema").Value == "2"; }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return false; }
        }

        public async Task LogOutAsync()
        {
            await storageService.RemoveTokenAsync();
        }
    }
}
