using Blazored.LocalStorage;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

internal class IdentityLocalStorageService : IIdentityStore
{
    private readonly ILocalStorageService localStorage;
    private readonly ISyncLocalStorageService syncLocalStorage;
    private readonly TokenRefreshService tokenRefreshService;

    private const string tokenStorageKey = "token";

    public IdentityLocalStorageService(ILocalStorageService localStorage, ISyncLocalStorageService syncLocalStorage, TokenRefreshService tokenRefreshService)
    {
        this.localStorage = localStorage;
        this.syncLocalStorage = syncLocalStorage;
        this.tokenRefreshService = tokenRefreshService;
    }

    public async Task StoreTokenAsync(TokenDTO tokenDto)
    {
        await localStorage.SetItemAsync(tokenStorageKey, tokenDto);
    }

    public async Task RemoveTokenAsync()
    {
        await localStorage.RemoveItemAsync(tokenStorageKey);
    }

    public string? GetToken()
    {
        return syncLocalStorage.GetItem<TokenDTO>(tokenStorageKey)?.Token;
    }

    public async Task<TokenDTO?> GetTokenAsync()
    {
        var tokenDto = await localStorage.GetItemAsync<TokenDTO>(tokenStorageKey);
        if (tokenDto == null || string.IsNullOrWhiteSpace(tokenDto.Token))
            return null;

        if (JwtUtils.IsExpired(tokenDto.Token, 5)) // 5 seconds early refresh
        {
            tokenDto = await tokenRefreshService.RefreshTokenAsync(tokenDto.RefreshToken);
            if (tokenDto is not null)
                await StoreTokenAsync(tokenDto);
        }

        return tokenDto;
    }
}