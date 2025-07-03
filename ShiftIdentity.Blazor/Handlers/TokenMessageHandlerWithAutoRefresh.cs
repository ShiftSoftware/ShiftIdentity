using Polly;
using Polly.Retry;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using System.Net;
using System.Net.Http.Headers;

namespace ShiftSoftware.ShiftIdentity.Blazor.Handlers;

public class TokenMessageHandlerWithAutoRefresh : DelegatingHandler
{
    private readonly AsyncRetryPolicy<HttpResponseMessage> _asyncPolicy;
    private readonly RetryPolicy<HttpResponseMessage> _policy;
    private readonly HttpMessageHandlerService httpMessageHandlerService;
    private readonly IIdentityStore tokenStore;
    private readonly MessageService msg;

    public TokenMessageHandlerWithAutoRefresh(HttpMessageHandlerService httpMessageHandlerService, IIdentityStore tokenProvider,
        MessageService msg)
    {
        //add this to solve "The inner handler has not been assigned"
        InnerHandler = new HttpClientHandler();

        this.httpMessageHandlerService = httpMessageHandlerService;
        tokenStore = tokenProvider;
        this.msg = msg;
        _asyncPolicy = Policy
            .Handle<HttpRequestException>()
            .OrResult<HttpResponseMessage>(r => r.StatusCode is HttpStatusCode.Unauthorized)
            .RetryAsync(async (_, _) =>
            {
                var success = await httpMessageHandlerService.RefreshAsync();

                if (!success.GetValueOrDefault())
                {
                    // Optionally log or handle the case when token refresh is not successful
                    // For example, you can throw a custom exception to prevent further retries.
                    throw new Exception("Token refresh failed.");
                }
            });

        _policy = Policy
            .Handle<HttpRequestException>()
            .OrResult<HttpResponseMessage>(r => r.StatusCode is HttpStatusCode.Unauthorized)
            .Retry(async (_, _) =>
            {
                var success = await httpMessageHandlerService.RefreshAsync();

                if (!success.GetValueOrDefault())
                {
                    // Optionally log or handle the case when token refresh is not successful
                    // For example, you can throw a custom exception to prevent further retries.
                    throw new Exception("Token refresh failed.");
                }
            });
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => await _asyncPolicy.ExecuteAsync(async () =>
        {
            var token = (await tokenStore.GetTokenAsync())?.Token ?? "";
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var result = await base.SendAsync(request, cancellationToken);

            if(result.IsSuccessStatusCode)
                await this.msg.RemoveWarningMessageAsync();

            return result;
        });

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    => _policy.Execute(() =>
    {
        var token = tokenStore.GetTokenAsync().Result?.Token ?? "";
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var result = base.Send(request, cancellationToken);

        if (result.IsSuccessStatusCode)
            this.msg.RemoveWarningMessageAsync().Wait();

        return result;
    });
}