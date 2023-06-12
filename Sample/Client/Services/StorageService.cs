using Blazored.LocalStorage;
using ShiftSoftware.ShiftIdentity.Blazor;
using ShiftSoftware.ShiftIdentity.Model;
using static System.Net.WebRequestMethods;
using System.Net.Http.Headers;

namespace Sample.Client.Services;

public class StorageService : IIdentityTokenStore, IIdentityTokenProvider
{
    private readonly ILocalStorageService localStorage;
    private const string tokenStorageKey = "token";
    private const string codeVerifierKey = "code-verifier";

    public StorageService(ILocalStorageService localStorage)
    {
        this.localStorage = localStorage;
    }

    public async Task StoreTokenAsync(TokenDTO token)
    {
        await localStorage.SetItemAsync<TokenDTO>(tokenStorageKey, token);
    }

    public async Task<TokenDTO> LoadTokenAsync()
    {
        return await localStorage.GetItemAsync<TokenDTO>(tokenStorageKey);
    }

    public async Task RemoveTokenAsync()
    {
        await localStorage.RemoveItemAsync(tokenStorageKey);
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

    public async Task<TokenDTO> GetTokenAsync()
    {
        return await LoadTokenAsync();
    }
}
