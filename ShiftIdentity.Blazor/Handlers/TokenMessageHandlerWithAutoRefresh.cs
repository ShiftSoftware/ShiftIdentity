using Polly;
using Polly.Retry;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using System.Net;
using System.Net.Http.Headers;

namespace ShiftSoftware.ShiftIdentity.Blazor.Handlers;

public class TokenMessageHandlerWithAutoRefresh : DelegatingHandler
{
    private readonly AsyncRetryPolicy<HttpResponseMessage> _policy;
    private readonly HttpMessageHandlerService httpMessageHandlerService;
    private readonly IIdentityStore tokenStore;

    public TokenMessageHandlerWithAutoRefresh(HttpMessageHandlerService httpMessageHandlerService, IIdentityStore tokenProvider)
    {
        //add this to solve "The inner handler has not been assigned"
        InnerHandler = new HttpClientHandler();

        this.httpMessageHandlerService = httpMessageHandlerService;
        tokenStore = tokenProvider;
        _policy = Policy
            .HandleResult<HttpResponseMessage>(r => r.StatusCode is HttpStatusCode.Unauthorized)
            .RetryAsync(async (_, _) => await httpMessageHandlerService.RefreshAsync());
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => await _policy.ExecuteAsync(async () =>
        {
            var token = (await tokenStore.GetTokenAsync())?.Token ?? "";
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            return await base.SendAsync(request, cancellationToken);
        });
}