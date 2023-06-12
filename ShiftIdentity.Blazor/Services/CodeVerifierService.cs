using Blazored.LocalStorage;
using System.Security.Cryptography;
using System.Text;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public class CodeVerifierService
{
    private readonly StorageService storageService;

    public CodeVerifierService(StorageService storageService)
    {
        this.storageService = storageService;
    }

    public async Task<string> GenerateCodeChallengeAsync()
    {
        var codeVerifier = Guid.NewGuid().ToString();

        await storageService.StoreCodeVerifierAsync(codeVerifier);

        var sha512 = SHA512.Create();

        var hash = sha512.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        var hashString = Convert.ToHexString(hash);

        return hashString;
    }

    public async Task<string> LoadCodeVerifierAsync()
    {
        return await storageService.LoadCodeVerifierAsync();
    }
}
