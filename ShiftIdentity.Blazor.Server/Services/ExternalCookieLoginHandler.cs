using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.ShiftEntity.Core.Extensions;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Helpers;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Services;

/// <summary>
/// Authenticates against an external identity server over HTTP (external identity hosting).
/// </summary>
internal sealed class ExternalCookieLoginHandler : ICookieLoginHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ExternalJwtValidator _jwtValidator;

    public ExternalCookieLoginHandler(IHttpClientFactory httpClientFactory, ExternalJwtValidator jwtValidator)
    {
        _httpClientFactory = httpClientFactory;
        _jwtValidator = jwtValidator;
    }

    public async Task<CookieLoginResult> LoginAsync(LoginDTO loginDto, HttpContext httpContext)
    {
        var client = _httpClientFactory.CreateClient("ShiftIdentityExternal");
        var loginUrl = client.BaseAddress!.ToString().AddUrlPath("Auth/Login");
        var response = await client.PostAsJsonAsync(loginUrl, loginDto);

        if (!response.IsSuccessStatusCode)
        {
            string? errorMessage = null;
            if (response.StatusCode != HttpStatusCode.InternalServerError)
            {
                try
                {
                    var error = await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>();
                    errorMessage = error?.Message?.Body;
                }
                catch
                {
                    // fallthrough to generic message
                }
            }
            return new CookieLoginResult(false, errorMessage ?? "Login failed", false);
        }

        var tokenResult = await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>();
        if (tokenResult?.Entity is null)
            return new CookieLoginResult(false, "Invalid response from identity server", false);

        try
        {
            _jwtValidator.Validate(tokenResult.Entity.Token);
        }
        catch (SecurityTokenException)
        {
            return new CookieLoginResult(false, "Invalid response from identity server", false);
        }

        await CookieAuthHelpers.SignInWithToken(httpContext, tokenResult.Entity);
        return new CookieLoginResult(true, null, tokenResult.Entity.RequirePasswordChange);
    }
}
