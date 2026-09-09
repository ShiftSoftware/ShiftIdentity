using System.Net.Http.Json;
using System.Text.Json;
using ShiftSoftware.ShiftIdentity.Core.Authentication;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public sealed partial class AuthenticationFlow
{
    public Task<AuthOutcome> RequestPasswordResetAsync(string identifier) => SecurityRequestAsync(
        "password-reset/request", new RequestSecurityEmail(identifier), result => result is SecurityDeliveryRequested);
    public Task<AuthOutcome> RequestEmailVerificationAsync(string identifier) => SecurityRequestAsync(
        "email-verification/request", new RequestSecurityEmail(identifier), result => result is SecurityDeliveryRequested);
    public Task<AuthOutcome> RequestCurrentEmailVerificationAsync(string access) => SecurityRequestAsync(
        "email-verification/request-current", new { }, result => result is SecurityDeliveryRequested, access);
    public Task<AuthOutcome> RequestAdminPasswordResetAsync(string access, long userID, bool manual = false) => SecurityRequestAsync(
        "password-reset/admin", new AdminPasswordResetRequest(userID, manual), result => manual
            ? result is SecurityDeliveryRequested || result is ManualPasswordResetIssued { Grant.Length: > 0 } issued && issued.ExpiresAt > clock.GetUtcNow()
            : result is SecurityDeliveryRequested, access);
    public Task<AuthOutcome> RequestAdminEmailVerificationAsync(string access, long userID) => SecurityRequestAsync(
        "email-verification/admin", new AdminEmailVerificationRequest(userID), result => result is SecurityDeliveryRequested, access);
    public Task<AuthOutcome> OpenSecurityLinkAsync(string grant, AuthenticationOperationPurpose purpose) => SecurityRequestAsync(
        "security-link/open", new OpenSecurityLinkRequest(grant, purpose), result =>
            result is SecurityLinkOpened { PageHandle.Length: > 0 } opened && opened.Purpose == purpose && opened.ExpiresAt > clock.GetUtcNow());
    public Task<AuthOutcome> CompletePasswordResetAsync(string pageHandle, string password) => SecurityRequestAsync(
        "password-reset/complete", new CompletePasswordResetRequest(pageHandle, password), result => result is ReturnToLogin);
    public Task<AuthOutcome> CompleteEmailVerificationAsync(string pageHandle) => SecurityRequestAsync(
        "email-verification/complete", new CompleteEmailVerificationRequest(pageHandle), result => result is EmailVerificationCompleted);

    // Grant requests never read, replace or remove the browser's ordinary session, including unexpected responses.
    private async Task<AuthOutcome> SecurityRequestAsync<T>(string route, T body, Func<AuthOutcome, bool> accepts, string? access = null)
    {
        if (Busy) return new AuthenticationRefused(AuthenticationFailure.InvalidRequest);
        Busy = true;
        var requestGeneration = generation;
        try
        {
            using var request = Request(route, body);
            if (access is not null) request.Headers.Authorization = new("Bearer", access);
            using var response = await http.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<AuthOutcome>();
            if (generation != requestGeneration) return new AuthenticationRefused(AuthenticationFailure.StaleOperation);
            if (result is AuthenticationRefused refused) return refused;
            return response.IsSuccessStatusCode && result is not null && accepts(result)
                ? result : new AuthenticationRefused(AuthenticationFailure.InvalidGrant);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException)
        { return new AuthenticationRefused(AuthenticationFailure.Unavailable); }
        finally { Busy = false; }
    }
}
