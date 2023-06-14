using Blazored.LocalStorage;
using ShiftSoftware.ShiftIdentity.Core.Models;
using System.Security.Cryptography;
using System.Text;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public class CodeVerifierService
{
    private readonly IIdentityStore tokenStore;

    public CodeVerifierService(IIdentityStore tokenStore)
    {
        this.tokenStore = tokenStore;
    }

    public async Task<string> GenerateCodeChallengeAsync()
    {
        var codeVerifier = Guid.NewGuid().ToString();

        await tokenStore.StoreCodeVerifierAsync(codeVerifier);

        var sha512 = SHA512.Create();

        var hash = sha512.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        var hashString = Convert.ToHexString(hash);

        return hashString;
    }

    public async Task<string> LoadCodeVerifierAsync()
    {
        return await tokenStore.LoadCodeVerifierAsync();
    }
}
