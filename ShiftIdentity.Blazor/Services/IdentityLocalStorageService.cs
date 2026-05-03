using Blazored.LocalStorage;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

internal class IdentityLocalStorageService : IIdentityStore
{
    private readonly ILocalStorageService localStorage;
    private readonly ISyncLocalStorageService syncLocalStorage;

    private const string tokenStorageKey = "token";

    public IdentityLocalStorageService(ILocalStorageService localStorage, ISyncLocalStorageService syncLocalStorage)
    {
        this.localStorage = localStorage;
        this.syncLocalStorage = syncLocalStorage;
    }

    public Task StoreTokenAsync(TokenDTO tokenDto)
        => localStorage.SetItemAsync(tokenStorageKey, tokenDto).AsTask();

    public Task RemoveTokenAsync()
        => localStorage.RemoveItemAsync(tokenStorageKey).AsTask();

    public string? GetToken()
        => syncLocalStorage.GetItem<TokenDTO>(tokenStorageKey)?.Token;

    public async Task<TokenDTO?> GetTokenAsync()
    {
        var tokenDto = await localStorage.GetItemAsync<TokenDTO>(tokenStorageKey);
        if (tokenDto == null || string.IsNullOrWhiteSpace(tokenDto.Token))
            return null;
        return tokenDto;
    }
}
