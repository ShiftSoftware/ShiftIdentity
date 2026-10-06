using System.Net;
using System.Net.Http.Json;
using ShiftSoftware.ShiftIdentity.Core.Authentication;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services;

/// <summary>
/// The states a preview page can show. Each one is what the staged authority answers for that case, so the real
/// screen renders it exactly as it would after a real email link.
/// </summary>
public enum PreviewState { Ready, Loading, InvalidLink, WeakPassword, CompletionFailed, Unavailable }

/// <summary>
/// Answers the staged authority's security-link routes in memory for the UI preview pages. Nothing leaves the browser:
/// no grant exists, no account is read or changed and no email is sent.
/// </summary>
internal sealed class PreviewAuthorityHandler(PreviewState state) : HttpMessageHandler
{
    // Long enough to see the busy states, short enough to iterate.
    private static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(600);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var route = request.RequestUri!.AbsolutePath;
        if (state == PreviewState.Loading) await Task.Delay(Timeout.Infinite, cancellationToken);
        await Task.Delay(Latency, cancellationToken);
        if (state == PreviewState.Unavailable) return new(HttpStatusCode.ServiceUnavailable);
        AuthOutcome outcome = route switch
        {
            _ when route.EndsWith("security-link/open") => state == PreviewState.InvalidLink
                ? Refused(AuthenticationFailure.InvalidGrant)
                : new SecurityLinkOpened("preview", "a***@example.com", await PurposeAsync(request, cancellationToken), DateTimeOffset.UtcNow.AddHours(1)),
            _ when route.EndsWith("password-reset/complete") => state switch
            {
                PreviewState.WeakPassword => Refused(AuthenticationFailure.InvalidNewPassword),
                PreviewState.CompletionFailed => Refused(AuthenticationFailure.InvalidGrant),
                _ => new ReturnToLogin()
            },
            _ when route.EndsWith("email-verification/complete") => state == PreviewState.CompletionFailed
                ? Refused(AuthenticationFailure.InvalidGrant) : new EmailVerificationCompleted(),
            _ => Refused(AuthenticationFailure.InvalidRequest)
        };
        return new(outcome is AuthenticationRefused ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
        {
            Content = JsonContent.Create(outcome)
        };
    }

    private static AuthenticationRefused Refused(AuthenticationFailure code) => new(code);

    private static async Task<AuthenticationOperationPurpose> PurposeAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        (await request.Content!.ReadFromJsonAsync<OpenSecurityLinkRequest>(cancellationToken))!.Purpose;
}
