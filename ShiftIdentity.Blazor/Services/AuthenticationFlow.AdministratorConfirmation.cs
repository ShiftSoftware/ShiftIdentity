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

    private async Task<AuthOutcome> ContinueAdministratorAsync<T>(AuthenticationStep step, string route, Func<string, string, T> body)
    {
        if (administratorSession is not { Access: { } access } expected || !await store.MatchesAsync(expected) ||
            Pending is not { Handle: { } handle, Purpose: AuthenticationOperationPurpose.AdministratorConfirmation } challenge ||
            challenge.Step != step || challenge.ExpiresAt <= clock.GetUtcNow() || verifier is not { } proof)
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
