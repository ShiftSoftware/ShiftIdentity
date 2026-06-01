using System.Net;
using System.Net.Http.Json;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.ShiftEntity.Core.Extensions;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Helpers;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Services;

/// <summary>
/// Refreshes tokens by calling the external identity server via HTTP (external identity hosting).
/// </summary>
internal sealed class ExternalCookieAuthManager : ICookieAuthManager
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ExternalJwtValidator _jwtValidator;

    public ExternalCookieAuthManager(IHttpClientFactory httpClientFactory, ExternalJwtValidator jwtValidator)
    {
        _httpClientFactory = httpClientFactory;
        _jwtValidator = jwtValidator;
    }

    public async Task<TokenDTO?> RefreshAsync(string refreshToken)
    {
        var client = _httpClientFactory.CreateClient("ShiftIdentityExternal");
        var response = await client.PostAsJsonAsync(client.BaseAddress!.ToString().AddUrlPath("Auth/Refresh"), new RefreshDTO { RefreshToken = refreshToken });

        // Only treat real auth rejections as "log the user out". 401/403 = the external
        // identity server rejected this refresh token. Everything else — 408/429 (transient),
        // 5xx (server), misconfigured 4xx — throws via EnsureSuccessStatusCode and is caught
        // by OnValidatePrincipal, preserving the existing session for the next retry. Without
        // this distinction, a brief rate-limit or outage would force-logout every user near
        // their cookie's refresh window.
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return null;

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>();
        if (result?.Entity is null)
            return null;

        try
        {
            _jwtValidator.Validate(result.Entity.Token);
        }
        catch (SecurityTokenException)
        {
            return null;
        }

        return result.Entity;
    }
}
