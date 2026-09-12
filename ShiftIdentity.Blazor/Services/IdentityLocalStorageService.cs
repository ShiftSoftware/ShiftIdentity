using Blazored.LocalStorage;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

internal sealed class IdentityLocalStorageService(ILocalStorageService localStorage,
    ISyncLocalStorageService syncLocalStorage, string storageKey = "token") : IIdentityTokenStorage
{
    public TokenDTO? Read()
    {
        try { return syncLocalStorage.GetItem<TokenDTO>(storageKey); }
        catch (System.Text.Json.JsonException) { syncLocalStorage.RemoveItem(storageKey); return null; }
    }

    public async Task<TokenDTO?> ReadAsync()
    {
        try { return await localStorage.GetItemAsync<TokenDTO>(storageKey); }
        catch (System.Text.Json.JsonException) { await localStorage.RemoveItemAsync(storageKey); return null; }
    }

    public async Task WriteAsync(TokenDTO tokenDto)
    {
        await localStorage.SetItemAsync(storageKey, tokenDto);
    }

    public async Task RemoveAsync()
    {
        await localStorage.RemoveItemAsync(storageKey);
    }
}
