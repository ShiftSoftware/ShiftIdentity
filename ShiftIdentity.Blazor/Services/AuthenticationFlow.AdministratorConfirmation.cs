using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using ShiftSoftware.ShiftIdentity.Core.Authentication;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public sealed partial class AuthenticationFlow
{
    public async Task<AuthOutcome> BeginAdministratorConfirmationAsync(string access)
    {
        var checkpoint = await store.ReadCheckpointAsync();
        if (checkpoint.Access != access) return new AuthenticationRefused(AuthenticationFailure.StaleOperation);
        return await SendAsync(() =>
        {
            Start();
            administratorSession = checkpoint;
            var request = Request("admin-confirmation", new StartPasswordChangeRequest(Challenge()));
            request.Headers.Authorization = new("Bearer", access);
            return request;
        });
    }

    public Task<AuthOutcome> ConfirmAdministratorPasswordAsync(string password) => ContinueAdministratorAsync(AuthenticationStep.Password,
        "admin-confirmation/password", (handle, proof) => new AdministratorPasswordProofRequest(handle, password, proof));

    public Task<AuthOutcome> ConfirmAdministratorMfaAsync(string code) => ContinueAdministratorAsync(AuthenticationStep.ExistingMfa,
        "admin-confirmation/mfa", (handle, proof) => new AdministratorMfaProofRequest(handle, code, proof));

    /// <summary>
    /// The provider sign-in that confirms the administrator (<see cref="AuthenticationChallenge.Provider"/>): the popup's
    /// address (<see cref="ProviderRedirect"/>), bound to the pending confirmation. The confirmation stays pending; complete
    /// it with <see cref="CompleteAdministratorProviderAsync"/> once the popup reports back.
    /// </summary>
    public async Task<AuthOutcome> StartAdministratorProviderAsync()
    {
        if (Busy) return new AuthenticationRefused(AuthenticationFailure.InvalidRequest);
        if (administratorSession is not { Access: { } access } expected || !await store.MatchesAsync(expected) ||
            Pending is not { Handle: { } handle, Purpose: AuthenticationOperationPurpose.AdministratorConfirmation, Provider: { } provider } challenge ||
            challenge.Step is not (AuthenticationStep.Password or AuthenticationStep.ProviderConfirmation) ||
            challenge.ExpiresAt <= clock.GetUtcNow() || verifier is not { } proof)
        {
            await CancelAsync();
            return new AuthenticationRefused(AuthenticationFailure.StaleOperation);
        }
        Busy = true;
        try
        {
            using var request = Request("admin-confirmation/provider/start", new AdministratorProviderConfirmationRequest(handle, proof));
            request.Headers.Authorization = new("Bearer", access);
            using var response = await http.SendAsync(request);
            var outcome = await response.Content.ReadFromJsonAsync<AuthOutcome>();
            if (response.IsSuccessStatusCode && outcome is ProviderRedirect { Url: { } url } redirect &&
                url.StartsWith(AuthorizeOrigin(provider), StringComparison.Ordinal)) return redirect;
            return outcome as AuthenticationRefused ?? new AuthenticationRefused(AuthenticationFailure.InvalidGrant);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException)
        { return new AuthenticationRefused(AuthenticationFailure.Unavailable); }
        finally { Busy = false; }
    }

    /// <summary>
    /// Completes a provider confirmation: the confirmed session, the Shift MFA step where the host asks for it, or
    /// <see cref="AuthenticationFailure.AuthorizationPending"/> while the provider has not returned (the confirmation stays pending).
    /// </summary>
    public Task<AuthOutcome> CompleteAdministratorProviderAsync() => ContinueAdministratorAsync(AuthenticationStep.ProviderConfirmation,
        "admin-confirmation/provider/complete", (handle, proof) => new AdministratorProviderConfirmationRequest(handle, proof));

    private async Task<AuthOutcome> ContinueAdministratorAsync<T>(AuthenticationStep step, string route, Func<string, string, T> body)
    {
        if (administratorSession is not { Access: { } access } expected || !await store.MatchesAsync(expected) ||
            Pending is not { Handle: { } handle, Purpose: AuthenticationOperationPurpose.AdministratorConfirmation } challenge ||
            // A confirmation that offers a provider beside the password completes through the provider from its password step.
            (challenge.Step != step && !(step == AuthenticationStep.ProviderConfirmation && challenge is { Step: AuthenticationStep.Password, Provider: not null })) ||
            challenge.ExpiresAt <= clock.GetUtcNow() || verifier is not { } proof)
        {
            await CancelAsync();
            return new AuthenticationRefused(AuthenticationFailure.StaleOperation);
        }
        return await SendAsync(() =>
        {
            var request = Request(route, body(handle, proof));
            request.Headers.Authorization = new("Bearer", access);
            return request;
        });
    }

    private static bool SameAdministrator(string original, string confirmed)
    {
        try { return new JsonWebToken(original).GetClaim("sub").Value == new JsonWebToken(confirmed).GetClaim("sub").Value; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return false; }
    }
}
