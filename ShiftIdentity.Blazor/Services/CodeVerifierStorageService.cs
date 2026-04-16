using System.Net.Http.Json;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public class CodeVerifierStorageService
{
    private readonly HttpClient httpClient;

    // In-memory fallback for when the code verifier is stored on the server via cookie
    // and we need to read it back during the same session.
    private string? inMemoryCodeVerifier;

    public CodeVerifierStorageService(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task StoreCodeVerifierAsync(string codeVerifier)
    {
        inMemoryCodeVerifier = codeVerifier;

        // Store in server-side HttpOnly cookie via endpoint
        try
        {
            await httpClient.PostAsJsonAsync("/api/identity/store-code-verifier",
                new { CodeVerifier = codeVerifier });
        }
        catch
        {
            // If endpoint doesn't exist (e.g. standalone WASM without server),
            // the in-memory value is still available for this session.
        }
    }

    public Task<string?> LoadCodeVerifierAsync()
    {
        // The code verifier is read server-side from the cookie in the /Auth/Token endpoint.
        // This method is only called from the client side as a fallback.
        return Task.FromResult(inMemoryCodeVerifier);
    }

    public Task RemoveCodeVerifierAsync()
    {
        inMemoryCodeVerifier = null;
        return Task.CompletedTask;
    }
}
