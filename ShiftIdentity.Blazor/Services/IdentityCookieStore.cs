using Blazored.LocalStorage;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

/// <summary>
/// Keeps the access token in local storage and the refresh token in a cookie, so apps served from the
/// configured cookie domain share a single session.
/// </summary>
internal class IdentityCookieStore : IdentityStoreBase
{
    private const string RefreshTokenStorageKey = "refresh-token";

    private readonly CookieService cookieService;
    private readonly ShiftIdentityBlazorOptions options;

    public IdentityCookieStore(ILocalStorageService localStorage, ISyncLocalStorageService syncLocalStorage,
        TokenRefreshService tokenRefreshService, CookieService cookieService, ShiftIdentityBlazorOptions options)
        : base(localStorage, syncLocalStorage, tokenRefreshService)
    {
        this.cookieService = cookieService;
        this.options = options;
    }

    protected override async Task<TokenDTO?> ReadAsync()
    {
        var tokenDto = await localStorage.GetItemAsync<TokenDTO>(TokenStorageKey);
        var refreshToken = await cookieService.GetItemAsStringAsync(RefreshTokenStorageKey);

        if (tokenDto is null && string.IsNullOrWhiteSpace(refreshToken))
            return null;

        // A cookie with no local token means a sibling app on the shared domain signed in. Handing back a
        // token carrying only the refresh token lets the base class exchange it for a full one.
        tokenDto ??= new TokenDTO();

        // The cookie is authoritative, and its max-age is what enforces the refresh token lifetime. Once it
        // has lapsed the refresh token is cleared, even though a copy still sits in the stored token.
        tokenDto.RefreshToken = refreshToken!;

        return tokenDto;
    }

    public override async Task StoreTokenAsync(TokenDTO tokenDto)
    {
        // Step-up tokens (change-password / MFA) carry no refresh token. Writing one here would evict the
        // cookie and take every sibling app on the shared domain down with it.
        if (!string.IsNullOrWhiteSpace(tokenDto.RefreshToken))
        {
            await cookieService.SetItemAsStringAsync(RefreshTokenStorageKey, tokenDto.RefreshToken, options.CookieDomain, "/",
                (int)tokenDto.RefreshTokenLifeTimeInSeconds.GetValueOrDefault());
        }

        await localStorage.SetItemAsync(TokenStorageKey, tokenDto);
    }

    public override async Task RemoveTokenAsync()
    {
        await cookieService.RemoveItemAsync(RefreshTokenStorageKey, options.CookieDomain, "/");

        await localStorage.RemoveItemAsync(TokenStorageKey);
    }
}
