using Blazored.LocalStorage;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services
{
    public class StorageService
    {
        private readonly ILocalStorageService localStorage;
        private readonly ISyncLocalStorageService syncLocalStorage;

        private const string tokenStorageKey = "token";

        public StorageService(
            ILocalStorageService localStorage,
            ISyncLocalStorageService syncLocalStorage)
        {
            this.localStorage = localStorage;
            this.syncLocalStorage = syncLocalStorage;
        }

        public async Task StoreTokenAsync(TokenDTO tokenDto)
        {
            await localStorage.SetItemAsync<TokenDTO>(tokenStorageKey, tokenDto);
        }

        public async Task<TokenDTO> GetTokenAsync()
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
    }
}
