using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using System.Net.Http.Json;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public class TokenRefreshService
{
    private readonly HttpClient http;

    public TokenRefreshService(ShiftIdentityHttpClient http)
    {
        this.http = http;
    }

    public async Task<TokenDTO?> RefreshTokenAsync(string refreshToken)
    {
        using var response = await http.PostAsJsonAsync<RefreshDTO>("auth/Refresh", new RefreshDTO { RefreshToken = refreshToken });
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())?.Entity;

        return null;
    }
}
