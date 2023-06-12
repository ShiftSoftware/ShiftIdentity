using Polly;
using Polly.Retry;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Model;
using System.Net;
using System.Net.Http.Headers;

namespace ShiftSoftware.ShiftIdentity.Blazor;

public class RefreshTokenMessageHandler : DelegatingHandler
{
    private readonly AsyncRetryPolicy<HttpResponseMessage> _policy;
    private readonly HttpMessageHandlerService httpMessageHandlerService;
    private readonly IIdentityTokenProvider tokenProvider;

    public RefreshTokenMessageHandler(HttpMessageHandlerService httpMessageHandlerService, IIdentityTokenProvider tokenProvider)
    {
        //add this to solve "The inner handler has not been assigned"
        InnerHandler = new HttpClientHandler();

        this.httpMessageHandlerService = httpMessageHandlerService;
        this.tokenProvider = tokenProvider;
        _policy = Polly.Policy
            .HandleResult<HttpResponseMessage>(r => r.StatusCode is HttpStatusCode.Unauthorized)
            .RetryAsync(async (_, _) => await httpMessageHandlerService.RefreshAsync());
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => await _policy.ExecuteAsync(async () =>
        {
            var token = (await tokenProvider.GetTokenAsync())?.Token ?? "";
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            return await base.SendAsync(request, cancellationToken);
        });
}