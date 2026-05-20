using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using ShiftSoftware.ShiftEntity.Core.Extensions;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Helpers;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Services;

/// <summary>
/// External hosting: changes the password against the external identity server, then re-issues
/// the local cookie with a fresh JWT so the <c>RequirePasswordChange</c> claim clears immediately.
///
/// The external <c>PUT /api/UserManager/ChangePassword</c> endpoint returns the updated user
/// data, not a token, so we sequence three calls:
/// <list type="number">
///   <item>POST <c>/Auth/Refresh</c> with the cookie's stored refresh token → fresh JWT for Bearer auth.</item>
///   <item>PUT <c>/api/UserManager/ChangePassword</c> with that JWT → success.</item>
///   <item>POST <c>/Auth/Refresh</c> again → fresh JWT now carrying <c>RequirePasswordChange=false</c>.</item>
/// </list>
/// The final JWT is bound to a new cookie via <see cref="CookieAuthHelpers.SignInWithToken"/>.
/// Credentials never traverse the WASM boundary; the user's password lives only in the form
/// post that hits this handler.
/// </summary>
internal sealed class ExternalCookieChangePasswordHandler : ICookieChangePasswordHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ExternalCookieChangePasswordHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<CookieChangePasswordResult> ChangePasswordAsync(ChangePasswordDTO dto, HttpContext httpContext)
    {
        var authResult = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var refreshToken = authResult.Properties?.GetString("refresh_token");
        if (string.IsNullOrEmpty(refreshToken))
            return new CookieChangePasswordResult(false, "Not authenticated");

        var client = _httpClientFactory.CreateClient("ShiftIdentityExternal");
        var baseUrl = client.BaseAddress!.ToString();

        // 1. Refresh to get a usable Bearer JWT for the change-password call.
        var preToken = await RefreshAsync(client, baseUrl, refreshToken);
        if (preToken is null)
            return new CookieChangePasswordResult(false, "Session is invalid; please log in again.");

        // 2. Call ChangePassword on the external server using the Bearer JWT.
        var changeRequest = new HttpRequestMessage(HttpMethod.Put, baseUrl.AddUrlPath("UserManager/ChangePassword"))
        {
            Content = JsonContent.Create(dto),
        };
        changeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", preToken.Token);

        var changeResponse = await client.SendAsync(changeRequest);
        if (!changeResponse.IsSuccessStatusCode)
        {
            string? errorMessage = null;
            if (changeResponse.StatusCode != HttpStatusCode.InternalServerError)
            {
                try
                {
                    var error = await changeResponse.Content.ReadFromJsonAsync<ShiftEntityResponse<object>>();
                    errorMessage = error?.Message?.Body;
                }
                catch { /* fallthrough */ }
            }
            return new CookieChangePasswordResult(false, errorMessage ?? "Password change failed");
        }

        // 3. Refresh again — the user entity now has RequireChangePassword=false, so the
        //    new JWT will carry RequirePasswordChange=false. Bind it to a new cookie.
        var freshToken = await RefreshAsync(client, baseUrl, preToken.RefreshToken);
        if (freshToken is null)
            return new CookieChangePasswordResult(false, "Password changed but failed to refresh session; please log in again.");

        await CookieAuthHelpers.SignInWithToken(httpContext, freshToken);
        return new CookieChangePasswordResult(true, null);
    }

    public async Task<CookieChangePasswordResult> CompletePasswordChangeAsync(CompletePasswordChangeDTO dto, HttpContext httpContext)
    {
        // The cookie carries the challenge JWT directly (no refresh token in challenge state).
        // Read it from AuthenticationProperties and use it as the Bearer for the external call.
        var authResult = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var challengeToken = authResult.Properties?.GetString("access_token");
        if (string.IsNullOrEmpty(challengeToken))
            return new CookieChangePasswordResult(false, "Not authenticated");

        var client = _httpClientFactory.CreateClient("ShiftIdentityExternal");
        var baseUrl = client.BaseAddress!.ToString();

        var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.AddUrlPath("Auth/CompletePasswordChange"))
        {
            Content = JsonContent.Create(dto),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", challengeToken);

        var response = await client.SendAsync(request);
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
                catch { /* fallthrough */ }
            }
            return new CookieChangePasswordResult(false, errorMessage ?? "Password change failed");
        }

        var tokenResult = await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>();
        if (tokenResult?.Entity is null)
            return new CookieChangePasswordResult(false, "Invalid response from identity server");

        // Upgrade the challenge cookie to a full session cookie.
        await CookieAuthHelpers.SignInWithToken(httpContext, tokenResult.Entity);
        return new CookieChangePasswordResult(true, null);
    }

    private static async Task<TokenDTO?> RefreshAsync(HttpClient client, string baseUrl, string refreshToken)
    {
        var response = await client.PostAsJsonAsync(
            baseUrl.AddUrlPath("Auth/Refresh"),
            new RefreshDTO { RefreshToken = refreshToken });

        if (!response.IsSuccessStatusCode)
            return null;

        var result = await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>();
        return result?.Entity;
    }
}
