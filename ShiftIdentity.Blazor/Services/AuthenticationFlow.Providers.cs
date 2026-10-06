using System.Net.Http.Json;
using System.Text.Json;
using ShiftSoftware.ShiftIdentity.Core.Authentication;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public sealed partial class AuthenticationFlow
{
    /// <summary>
    /// The sign-in providers the host turned on. Best effort: whatever goes wrong reading them (an older API without the
    /// route included), the login screen shows no provider and password sign-in is unaffected.
    /// </summary>
    public async Task<SignInProviders> ReadProvidersAsync()
    {
        try { return await http.GetFromJsonAsync<SignInProviders>("api/identity/v2/providers") ?? new(false); }
        catch (Exception) { return new(false); }
    }

    /// <summary>
    /// The provider accounts that can sign in to the signed-in account, or to <paramref name="userKey"/>'s for an
    /// operator. A read only; it changes no pending operation and never touches stored credentials.
    /// </summary>
    public async Task<AuthOutcome> ReadProviderLinksAsync(string access, string? userKey = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/identity/v2/providers/links" +
                (userKey is null ? "" : "/" + Uri.EscapeDataString(userKey)));
            request.Headers.Authorization = new("Bearer", access);
            using var response = await http.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<AuthOutcome>();
            return response.IsSuccessStatusCode && result is ProviderLinksRead links ? links
                : result as AuthenticationRefused ?? new AuthenticationRefused(AuthenticationFailure.InvalidGrant);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException)
        { return new AuthenticationRefused(AuthenticationFailure.Unavailable); }
    }

    /// <summary>
    /// Starts a Microsoft sign-in. On success the caller keeps <paramref name="verifier"/> for the page's return (the
    /// browser leaves for Microsoft, so this flow's memory does not survive) and sends the browser to the redirect.
    /// </summary>
    public async Task<(AuthOutcome Outcome, string? Verifier)> StartMicrosoftAsync()
    {
        if (Busy) return (new AuthenticationRefused(AuthenticationFailure.InvalidRequest), null);
        Start();
        var proof = verifier;
        Busy = true;
        try
        {
            using var response = await http.SendAsync(Request("providers/microsoft/start", new StartProviderSignInRequest(Challenge())));
            var outcome = await response.Content.ReadFromJsonAsync<AuthOutcome>();
            if (response.IsSuccessStatusCode && outcome is ProviderRedirect { Url: { } url } redirect &&
                url.StartsWith("https://login.microsoftonline.com/", StringComparison.Ordinal))
                return (redirect, proof);
            Restart();
            return (outcome as AuthenticationRefused ?? new AuthenticationRefused(AuthenticationFailure.InvalidGrant), null);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException)
        {
            Restart();
            return (new AuthenticationRefused(AuthenticationFailure.Unavailable), null);
        }
        finally { Busy = false; }
    }

    /// <summary>
    /// Completes a Microsoft sign-in with the handle the return carried and the verifier kept when it started. The
    /// answer is a session, or a Shift MFA step that continues in this flow like a password sign-in's.
    /// </summary>
    public Task<AuthOutcome> CompleteMicrosoftAsync(string handle, string startedVerifier) => SendAsync(() =>
    {
        Start();
        verifier = startedVerifier;
        return Request("providers/microsoft/complete", new CompleteProviderSignInRequest(handle, startedVerifier));
    });
}
