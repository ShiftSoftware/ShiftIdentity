using System.Net.Http.Json;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Services;

/// <summary>
/// Refreshes tokens by calling the external identity server via HTTP (external identity hosting).
/// </summary>
public class ExternalCookieAuthManager : ICookieAuthManager
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ExternalCookieAuthManager(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<TokenDTO?> RefreshAsync(string refreshToken)
    {
        var client = _httpClientFactory.CreateClient("ShiftIdentityExternal");
        var response = await client.PostAsJsonAsync("api/Auth/Refresh", new RefreshDTO { RefreshToken = refreshToken });

        if (!response.IsSuccessStatusCode)
            return null;

        var result = await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>();
        return result?.Entity;
    }
}
