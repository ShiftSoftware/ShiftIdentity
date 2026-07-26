using ShiftSoftware.ShiftIdentity.Blazor.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;

namespace ShiftSoftware.ShiftIdentity.Blazor.Handlers;

public class TokenMessageHandlerWithAutoRefresh : DelegatingHandler
{
    private readonly IIdentityStore tokenStore;
    private readonly MessageService msg;

    // On a dead-session 401 (the refresh token was rejected upstream, so an empty bearer went out) we remove
    // the stored token and show this banner. Removing it is essential, not optional: while the dead token
    // lingers, the sync GetToken() used for auth state does no expiry check, so the app still believes the
    // user is signed in — every request 401s AND the login page bounces them back to "/" as already-authed,
    // a deadlock where they can neither use the app nor sign in. Removal does NOT redirect this tab (auth
    // state isn't re-notified, so an in-progress form / filtered list survives); it only lets a fresh load —
    // the banner's "another tab" link — reach login. The success branch clears the banner once a session
    // returns (e.g. after they sign in on the other tab).
    private const string SessionExpiredMessage = "Your session has expired. Please login again (in another tab). ";
    private const string SessionExpiredLinkText = "Login another tab";

    public TokenMessageHandlerWithAutoRefresh(IIdentityStore tokenProvider, MessageService msg)
    {
        //add this to solve "The inner handler has not been assigned"
        InnerHandler = new HttpClientHandler();

        tokenStore = tokenProvider;
        this.msg = msg;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = (await tokenStore.GetTokenAsync())?.Token ?? "";
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var result = await base.SendAsync(await CloneRequestIfNeededAsync(request), cancellationToken);

        if(result.IsSuccessStatusCode)
            await this.msg.RemoveWarningMessageAsync();
        else if (result.StatusCode == HttpStatusCode.Unauthorized && string.IsNullOrWhiteSpace(token))
        {
            await tokenStore.RemoveTokenAsync();
            await this.msg.ShowWarningMessageAsync(SessionExpiredMessage, SessionExpiredLinkText);
        }

        return result;
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = tokenStore.GetTokenAsync().Result?.Token ?? "";
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var result = base.Send(CloneRequestIfNeededAsync(request).GetAwaiter().GetResult(), cancellationToken);

        if (result.IsSuccessStatusCode)
            this.msg.RemoveWarningMessageAsync().Wait();
        else if (result.StatusCode == HttpStatusCode.Unauthorized && string.IsNullOrWhiteSpace(token))
        {
            tokenStore.RemoveTokenAsync().Wait();
            this.msg.ShowWarningMessageAsync(SessionExpiredMessage, SessionExpiredLinkText).Wait();
        }

        return result;
    }

    private async Task<HttpRequestMessage> CloneRequestIfNeededAsync(HttpRequestMessage request)
    {
        // Only clone if we have content that might be read-once
        if (request.Content == null)
            return request;

        // Create a new request with the same properties
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version
        };

        // Copy headers
        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        // Clone content if present
        if (request.Content != null)
        {
            var contentBytes = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(contentBytes);

            // Copy content headers
            foreach (var header in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Copy properties
        foreach (var prop in request.Options)
            clone.Options.TryAdd(prop.Key, prop.Value);

        return clone;
    }
}