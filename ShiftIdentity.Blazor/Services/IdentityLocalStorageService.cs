using Blazored.LocalStorage;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

internal class IdentityLocalStorageService : IIdentityStore
{
    private readonly ILocalStorageService localStorage;
    private readonly ISyncLocalStorageService syncLocalStorage;

    private const string tokenStorageKey = "token";
    private const string codeVerifierKey = "code-verifier";

    public IdentityLocalStorageService(ILocalStorageService localStorage, ISyncLocalStorageService syncLocalStorage)
    {
        this.localStorage = localStorage;
        this.syncLocalStorage = syncLocalStorage;
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
        return await localStorage.GetItemAsync<TokenDTO>(tokenStorageKey);
    }
}