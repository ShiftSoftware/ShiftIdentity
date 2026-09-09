using System.Net.Http.Json;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

/// <summary>Explicit host opt-in for routed account screens. Hosts must supply only the staged authority.</summary>
public sealed class AdmissionUiContext(AuthenticationFlow flow, IIdentityStore store, HttpClient http)
{
    public AuthenticationFlow Flow { get; } = flow;
    public IIdentityStore Store { get; } = store;
    public Task<AdmissionAccount?> AccountAsync(long? userID = null) => ReadAsync<AdmissionAccount>(
        "account" + (userID is null ? "" : "/" + userID));
    public async Task<AdmissionAccount[]> UsersAsync() => await ReadAsync<AdmissionAccount[]>("users") ?? [];
    private async Task<T?> ReadAsync<T>(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/identity/v2/" + path);
        request.Headers.Authorization = new("Bearer", (await Store.GetTokenAsync())?.Token);
        using var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>() : default;
    }
}

public sealed record AdmissionAccount(long UserID, string Username, string FullName,
    bool HasAuthenticator, bool RecoveryRequired, bool CanManageRecovery,
    string? Email = null, bool EmailVerified = false, bool RecoveryEmailEligible = false,
    bool CanManagePasswordReset = false, bool CanManageEmailVerification = false);
