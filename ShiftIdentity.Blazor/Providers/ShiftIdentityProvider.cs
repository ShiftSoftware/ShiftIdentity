using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using System.Net.Http.Json;

namespace ShiftSoftware.ShiftIdentity.Blazor.Providers;

internal class ShiftIdentityProvider : IShiftIdentityProvider
{
    private readonly HttpClient http;

    public ShiftIdentityProvider(HttpClient http)
    {
        this.http = http;
    }

    public async Task<HttpResponse<ShiftEntityResponse<TokenDTO?>?>> GetTokenWithAppIdOnlyAsync(string baseUrl, GenerateExternalTokenWithAppIdOnlyDTO dto)
    {
        var url = $"{(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/")}Auth/TokenWithAppIdOnly";
        using var response = await http.PostAsJsonAsync(url, dto);

        return new HttpResponse<ShiftEntityResponse<TokenDTO?>?>
            ((await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!, response.StatusCode);
    }

    /// <summary>
    /// Refresh the token by sending refresh token
    /// </summary>
    /// <param name="baseUrl"></param>
    /// <param name="dto"></param>
    /// <returns></returns>
    public async Task<HttpResponse<ShiftEntityResponse<TokenDTO?>?>> RefreshTokenAsync(string baseUrl, RefreshDTO dto)
    {
        var url = $"{(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/")}Auth/Refresh";
        using var response = await http.PostAsJsonAsync(url, dto);

        return new HttpResponse<ShiftEntityResponse<TokenDTO?>?>
            ((await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!, response.StatusCode);
    }

    public async Task<HttpResponse<ShiftEntityResponse<UserDataDTO?>?>> GetUserDataAsync(string baseUrl)
    {
        var url = $"{(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/")}UserManager/UserData";
        using var response = await http.GetAsync(url);

        return new HttpResponse<ShiftEntityResponse<UserDataDTO?>?>
            ((await response.Content.ReadFromJsonAsync<ShiftEntityResponse<UserDataDTO>>())!, response.StatusCode);
    }

    public async Task<HttpResponse<ShiftEntityResponse<UserDataDTO?>?>> UpdateUserDataAsync(string baseUrl, UserDataDTO dto)
    {
        var url = $"{(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/")}UserManager/UserData";
        using var response = await http.PutAsJsonAsync(url, dto);

        return new HttpResponse<ShiftEntityResponse<UserDataDTO?>?>
            ((await response.Content.ReadFromJsonAsync<ShiftEntityResponse<UserDataDTO>>())!, response.StatusCode);
    }

    public async Task<HttpResponse<ShiftEntityResponse<TokenDTO?>?>> LoginAsync(string baseUrl, LoginDTO dto)
    {
        var url = $"{(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/")}Auth/Login";
        using var response = await http.PostAsJsonAsync(url, dto);

        return new HttpResponse<ShiftEntityResponse<TokenDTO?>?>
            ((await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!, response.StatusCode);
    }
}
