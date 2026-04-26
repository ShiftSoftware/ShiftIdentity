using Microsoft.AspNetCore.Components;
using ShiftSoftware.ShiftEntity.Model;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Authorization;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Blazor;
using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services
{
    public class AuthService
    {
        private HttpService httpService;
        private readonly IIdentityStore storageService;
        private readonly AuthenticationStateProvider? authStateProvider;
        private readonly HttpClient http;
        private readonly NavigationManager navManager;
        private readonly ShiftIdentityBlazorOptions identityOptions;
        private const string url = "auth/";

        public AuthService(
            HttpService httpService,
            IIdentityStore storageService,
            AuthenticationStateProvider? authStateProvider,
            NavigationManager navManager,
            HttpClient http,
            ShiftIdentityBlazorOptions identityOptions)
        {
            this.httpService = httpService;
            this.storageService = storageService;
            this.authStateProvider = authStateProvider;
            this.navManager = navManager;
            this.http = http;
            this.identityOptions = identityOptions;
        }

        public async Task<HttpResponse<ShiftEntityResponse<TokenDTO>>> LoginAsync(LoginDTO loginDto)
        {
            if (identityOptions.UseCookieAuth)
            {
                if (identityOptions.HostingType == ShiftIdentityHostingTypes.External)
                {
                    return await LoginExternalCookieAuthAsync(loginDto);
                }

                return await LoginInternalCookieAuthAsync(loginDto);
            }

            return await httpService.PostAsync<ShiftEntityResponse<TokenDTO>, LoginDTO>(url + "login", loginDto);
        }

        /// <summary>
        /// Internal hosting + cookie auth: call the server's login endpoint which authenticates in-process and sets the cookie.
        /// </summary>
        private async Task<HttpResponse<ShiftEntityResponse<TokenDTO>>> LoginInternalCookieAuthAsync(LoginDTO loginDto)
        {
            var response = await http.PostAsJsonAsync("/api/identity/login", loginDto);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<CookieLoginResponse>();
                var token = new TokenDTO
                {
                    UserData = result?.UserData,
                    RequirePasswordChange = result?.RequirePasswordChange ?? false,
                };

                return new HttpResponse<ShiftEntityResponse<TokenDTO>>(
                    new ShiftEntityResponse<TokenDTO> { Entity = token },
                    HttpStatusCode.OK);
            }
            else
            {
                var error = await response.Content.ReadFromJsonAsync<CookieLoginErrorResponse>();
                return new HttpResponse<ShiftEntityResponse<TokenDTO>>(
                    new ShiftEntityResponse<TokenDTO>
                    {
                        Message = new Message { Body = error?.Error ?? "Login failed" },
                    },
                    response.StatusCode);
            }
        }

        /// <summary>
        /// External hosting + cookie auth: call the external identity server directly for login,
        /// then send the token to own server to set the auth cookie.
        /// </summary>
        private async Task<HttpResponse<ShiftEntityResponse<TokenDTO>>> LoginExternalCookieAuthAsync(LoginDTO loginDto)
        {
            // Call external identity server directly
            var externalBaseUrl = identityOptions.ExternalIdentityApiUrl?.TrimEnd('/') ?? throw new InvalidOperationException(
                "ExternalIdentityApiUrl must be configured for external hosting with cookie auth.");

            var loginResponse = await http.PostAsJsonAsync($"{externalBaseUrl}/Auth/Login", loginDto);

            if (!loginResponse.IsSuccessStatusCode)
            {
                var errorResult = await loginResponse.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>();
                return new HttpResponse<ShiftEntityResponse<TokenDTO>>(
                    new ShiftEntityResponse<TokenDTO>
                    {
                        Message = new Message { Body = errorResult?.Message?.Body ?? "Login failed" },
                    },
                    loginResponse.StatusCode);
            }

            var tokenResult = await loginResponse.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>();

            if (tokenResult?.Entity == null)
            {
                return new HttpResponse<ShiftEntityResponse<TokenDTO>>(
                    new ShiftEntityResponse<TokenDTO>
                    {
                        Message = new Message { Body = "Invalid response from identity server" },
                    },
                    HttpStatusCode.InternalServerError);
            }

            // Send token to own server to set the auth cookie
            var signInResponse = await http.PostAsJsonAsync("/api/identity/sign-in-with-token", new SignInWithTokenRequest
            {
                Token = tokenResult.Entity.Token,
                RefreshToken = tokenResult.Entity.RefreshToken,
                TokenLifeTimeInSeconds = tokenResult.Entity.TokenLifeTimeInSeconds,
            });

            if (!signInResponse.IsSuccessStatusCode)
            {
                return new HttpResponse<ShiftEntityResponse<TokenDTO>>(
                    new ShiftEntityResponse<TokenDTO>
                    {
                        Message = new Message { Body = "Failed to set auth cookie" },
                    },
                    signInResponse.StatusCode);
            }

            return new HttpResponse<ShiftEntityResponse<TokenDTO>>(
                new ShiftEntityResponse<TokenDTO> { Entity = tokenResult.Entity },
                HttpStatusCode.OK);
        }

        public async Task LogOutAsync()
        {
            if (identityOptions.UseCookieAuth)
            {
                try { await http.PostAsync("/api/identity/logout", null); } catch { }
            }

            await storageService.RemoveTokenAsync();
        }

        private class CookieLoginResponse
        {
            public TokenUserDataDTO? UserData { get; set; }
            public bool RequirePasswordChange { get; set; }
        }

        private class CookieLoginErrorResponse
        {
            public string? Error { get; set; }
        }

        private class SignInWithTokenRequest
        {
            public string Token { get; set; } = default!;
            public string RefreshToken { get; set; } = default!;
            public long? TokenLifeTimeInSeconds { get; set; }
        }
    }
}
