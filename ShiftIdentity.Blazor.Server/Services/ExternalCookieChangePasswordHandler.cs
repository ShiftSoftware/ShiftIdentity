using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.ShiftEntity.Core.Extensions;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Helpers;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Services;

/// <summary>
/// External hosting: changes the password against the external identity server, then re-issues
/// the local cookie with the fresh JWT the change returns.
///
/// <list type="number">
///   <item>POST <c>/Auth/Refresh</c> with the cookie's stored refresh token → fresh JWT for Bearer auth.</item>
///   <item>PUT <c>/api/UserManager/ChangePassword</c> with that JWT → fresh token carrying the rolled
///         security stamp and <c>RequirePasswordChange=false</c>.</item>
/// </list>
/// The returned JWT is signature-validated and bound to a new cookie via
/// <see cref="CookieAuthHelpers.SignInWithToken"/>. A second refresh is deliberately NOT issued:
/// changing the password rolls the user's security stamp, so the refresh token minted in step 1
/// (under the old stamp) is now rejected — the change-password response is the only token that
/// carries the new stamp. Credentials never traverse the WASM boundary; the user's password
/// lives only in the form post that hits this handler.
/// </summary>
internal sealed class ExternalCookieChangePasswordHandler : ICookieChangePasswordHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ExternalJwtValidator _jwtValidator;
    private readonly ICookieAuthManager _authManager;

    public ExternalCookieChangePasswordHandler(
        IHttpClientFactory httpClientFactory,
        ExternalJwtValidator jwtValidator,
        ICookieAuthManager authManager)
    {
        _httpClientFactory = httpClientFactory;
        _jwtValidator = jwtValidator;
        _authManager = authManager;
    }

    public async Task<CookieChangePasswordResult> ChangePasswordAsync(ChangePasswordDTO dto, HttpContext httpContext)
    {
        var authResult = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var refreshToken = authResult.Properties?.GetString("refresh_token");
        if (string.IsNullOrEmpty(refreshToken))
            return new CookieChangePasswordResult(false, "Not authenticated");

        // 1. Refresh to get a usable Bearer JWT for the change-password call. Shares the
        //    same RefreshAsync contract as OnValidatePrincipal: null = rejected (401/403),
        //    throw = transient (5xx/429/network).
        TokenDTO? preToken;
        try
        {
            preToken = await _authManager.RefreshAsync(refreshToken);
        }
        catch
        {
            return new CookieChangePasswordResult(false, "Could not reach identity server, please try again.");
        }
        if (preToken is null)
            return new CookieChangePasswordResult(false, "Session is invalid; please log in again.");

        // 2. Call ChangePassword on the external server using the Bearer JWT. The response carries
        //    a fresh token minted after the security stamp rolled (RequirePasswordChange=false).
        var client = _httpClientFactory.CreateClient("ShiftIdentityExternal");
        var changeRequest = new HttpRequestMessage(HttpMethod.Put, client.BaseAddress!.ToString().AddUrlPath("UserManager/ChangePassword"))
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
                    var error = await changeResponse.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>();
                    errorMessage = error?.Message?.Body;
                }
                catch { /* fallthrough */ }
            }
            return new CookieChangePasswordResult(false, errorMessage ?? "Password change failed");
        }

        // 3. Bind the returned token to a new cookie. Do NOT refresh again: the stamp has rolled,
        //    so preToken.RefreshToken (minted in step 1) is now invalid — this response is the only
        //    token carrying the new stamp. Validate its signature before trusting it.
        var tokenResult = await changeResponse.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>();
        if (tokenResult?.Entity is null)
            return new CookieChangePasswordResult(false, "Invalid response from identity server");

        try
        {
            _jwtValidator.Validate(tokenResult.Entity.Token);
        }
        catch (SecurityTokenException)
        {
            return new CookieChangePasswordResult(false, "Invalid response from identity server");
        }

        await CookieAuthHelpers.SignInWithToken(httpContext, tokenResult.Entity);
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

        try
        {
            _jwtValidator.Validate(tokenResult.Entity.Token);
        }
        catch (SecurityTokenException)
        {
            return new CookieChangePasswordResult(false, "Invalid response from identity server");
        }

        // Upgrade the challenge cookie to a full session cookie.
        await CookieAuthHelpers.SignInWithToken(httpContext, tokenResult.Entity);
        return new CookieChangePasswordResult(true, null);
    }
}
