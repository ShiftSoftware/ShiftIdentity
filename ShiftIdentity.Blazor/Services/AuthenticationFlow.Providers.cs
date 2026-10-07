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
    /// Starts a Microsoft sign-in. On success the caller keeps the verifier for the page's return and sends the
    /// browser to the redirect. The same as <see cref="StartProviderAsync"/> for Microsoft.
    /// </summary>
    public Task<(AuthOutcome Outcome, string? Verifier)> StartMicrosoftAsync() => StartProviderAsync(SignInProvider.Microsoft);

    /// <summary>
    /// Completes a Microsoft sign-in. The same as <see cref="CompleteProviderAsync"/> for Microsoft.
    /// </summary>
    public Task<AuthOutcome> CompleteMicrosoftAsync(string handle, string startedVerifier) =>
        CompleteProviderAsync(SignInProvider.Microsoft, handle, startedVerifier);

    /// <summary>The provider's name in its routes and in the login screen's return fragment.</summary>
    public static string RouteName(SignInProvider provider) => provider switch
    {
        SignInProvider.Microsoft => "microsoft",
        SignInProvider.Google => "google",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    // The only place each provider's sign-in may send the browser.
    private static string AuthorizeOrigin(SignInProvider provider) => provider switch
    {
        SignInProvider.Microsoft => "https://login.microsoftonline.com/",
        SignInProvider.Google => "https://accounts.google.com/",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    /// <summary>
    /// Starts a provider sign-in. On success the caller keeps <paramref name="verifier"/> for the page's return (the
    /// browser leaves for the provider, so this flow's memory does not survive) and sends the browser to the redirect.
    /// </summary>
    public async Task<(AuthOutcome Outcome, string? Verifier)> StartProviderAsync(SignInProvider provider)
    {
        if (Busy) return (new AuthenticationRefused(AuthenticationFailure.InvalidRequest), null);
        Start();
        var proof = verifier;
        Busy = true;
        try
        {
            using var response = await http.SendAsync(Request($"providers/{RouteName(provider)}/start", new StartProviderSignInRequest(Challenge())));
            var outcome = await response.Content.ReadFromJsonAsync<AuthOutcome>();
            if (response.IsSuccessStatusCode && outcome is ProviderRedirect { Url: { } url } redirect &&
                url.StartsWith(AuthorizeOrigin(provider), StringComparison.Ordinal))
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
    /// Completes a provider sign-in with the handle the return carried and the verifier kept when it started. The
    /// answer is a session, or a Shift MFA step that continues in this flow like a password sign-in's.
    /// </summary>
    public Task<AuthOutcome> CompleteProviderAsync(SignInProvider provider, string handle, string startedVerifier) => SendAsync(() =>
    {
        Start();
        verifier = startedVerifier;
        return Request($"providers/{RouteName(provider)}/complete", new CompleteProviderSignInRequest(handle, startedVerifier));
    });
}
