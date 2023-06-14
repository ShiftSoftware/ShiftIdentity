using Blazored.LocalStorage;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Models;
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

    public async Task<TokenDTO> LoadTokenAsync()
    {
        return await localStorage.GetItemAsync<TokenDTO>(tokenStorageKey);
    }

    public TokenDTO LoadToken()
    {
        return syncLocalStorage.GetItem<TokenDTO>(tokenStorageKey);
    }

    public async Task RemoveTokenAsync()
    {
        await localStorage.RemoveItemAsync(tokenStorageKey);
    }

    public string? GetToken()
    {
        return LoadToken()?.Token;
    }

    public async Task<TokenDTO> GetTokenAsync()
    {
        return await LoadTokenAsync();
    }

    public async Task StoreCodeVerifierAsync(string codeVerifier)
    {
        await localStorage.SetItemAsStringAsync(codeVerifierKey, codeVerifier);
    }

    public async Task<string> LoadCodeVerifierAsync()
    {
        return await localStorage.GetItemAsStringAsync(codeVerifierKey);
    }

    public async Task RemoveCodeVerifierAsync()
    {
        await localStorage.RemoveItemAsync(codeVerifierKey);
    }
}