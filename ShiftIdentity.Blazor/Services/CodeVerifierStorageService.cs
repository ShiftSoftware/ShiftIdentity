using Blazored.LocalStorage;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public class CodeVerifierStorageService
{
    private readonly ILocalStorageService localStorage;

    private const string codeVerifierKey = "code-verifier";

    public CodeVerifierStorageService(ILocalStorageService localStorage)
    {
        this.localStorage = localStorage;
    }

    public async Task StoreCodeVerifierAsync(string codeVerifier)
    {
        await localStorage.SetItemAsStringAsync(codeVerifierKey, codeVerifier);
    }

    public async Task<string?> LoadCodeVerifierAsync()
    {
        return await localStorage.GetItemAsStringAsync(codeVerifierKey);
    }

    public async Task RemoveCodeVerifierAsync()
    {
        await localStorage.RemoveItemAsync(codeVerifierKey);
    }
}
