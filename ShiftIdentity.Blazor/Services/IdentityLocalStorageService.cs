using Blazored.LocalStorage;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

internal class IdentityLocalStorageService : IdentityStoreBase
{
    public IdentityLocalStorageService(ILocalStorageService localStorage, ISyncLocalStorageService syncLocalStorage,
        TokenRefreshService tokenRefreshService)
        : base(localStorage, syncLocalStorage, tokenRefreshService)
    {
    }

    protected override async Task<TokenDTO?> ReadAsync()
    {
        return await localStorage.GetItemAsync<TokenDTO>(TokenStorageKey);
    }

    public override async Task StoreTokenAsync(TokenDTO tokenDto)
    {
        await localStorage.SetItemAsync(TokenStorageKey, tokenDto);
    }

    public override async Task RemoveTokenAsync()
    {
        await localStorage.RemoveItemAsync(TokenStorageKey);
    }
}
