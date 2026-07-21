using Blazored.LocalStorage;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Enums;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

/// <summary>
/// Shared token store behaviour. The access token always lives in local storage, and a token that is
/// missing or expired is refreshed when read. Derived types decide where the refresh token is kept.
/// </summary>
internal abstract class IdentityStoreBase : IIdentityStore
{
    protected const string TokenStorageKey = "token";

    protected readonly ILocalStorageService localStorage;
    protected readonly ISyncLocalStorageService syncLocalStorage;
    private readonly TokenRefreshService tokenRefreshService;

    protected IdentityStoreBase(ILocalStorageService localStorage, ISyncLocalStorageService syncLocalStorage,
        TokenRefreshService tokenRefreshService)
    {
        this.localStorage = localStorage;
        this.syncLocalStorage = syncLocalStorage;
        this.tokenRefreshService = tokenRefreshService;
    }

    public string? GetToken()
    {
        return syncLocalStorage.GetItem<TokenDTO>(TokenStorageKey)?.Token;
    }

    public async Task<TokenDTO?> GetTokenAsync()
    {
        var tokenDto = await ReadAsync();

        if (tokenDto is null)
            return null;

        // During a step-up phase (forced change-password / MFA / MFA enrollment) the stored token is a
        // short-lived, purpose-bound temporary token with no refresh token. Refreshing it would fail and
        // wipe the token, kicking the user out of the flow.
        if (tokenDto.Flow != AuthPurpose.None || string.IsNullOrWhiteSpace(tokenDto.RefreshToken))
            return tokenDto;

        // An empty access token alongside a usable refresh token is how a session arrives from a sibling
        // app on a shared cookie domain, so refresh on that too rather than only on expiry.
        if (string.IsNullOrWhiteSpace(tokenDto.Token) || JwtUtils.IsExpired(tokenDto.Token, 10)) // 10 seconds early refresh
        {
            var refreshed = await tokenRefreshService.RefreshTokenAsync(tokenDto.RefreshToken);

            // A null result means the server rejected the refresh token, so the session really is over.
            // Returning the expired token instead would just turn into a 401 further down.
            if (refreshed is null)
                return null;

            await StoreTokenAsync(refreshed);

            return refreshed;
        }

        return tokenDto;
    }

    /// <summary>
    /// Reads whatever is stored, or null when there is nothing usable to build a session from.
    /// </summary>
    protected abstract Task<TokenDTO?> ReadAsync();

    public abstract Task StoreTokenAsync(TokenDTO token);

    public abstract Task RemoveTokenAsync();
}
