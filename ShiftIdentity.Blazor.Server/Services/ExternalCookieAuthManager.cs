using System.Net;
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

        // if the response is a client error (4xx), return null to indicate that the refresh failed (e.g., invalid refresh token)
        // if the response is a server error (5xx), throw an exception to indicate that there was a problem with the identity server
        if (response.StatusCode >= HttpStatusCode.BadRequest && response.StatusCode < HttpStatusCode.InternalServerError)
            return null;

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>();
        return result?.Entity;
    }
}
