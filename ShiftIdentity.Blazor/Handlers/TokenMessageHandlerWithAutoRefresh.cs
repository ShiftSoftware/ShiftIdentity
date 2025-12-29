using Polly;
using Polly.Retry;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;

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
            .RetryAsync(1, async (_, _) =>
            {
                var success = await httpMessageHandlerService.RefreshAsync();

                if (!success.GetValueOrDefault())
                    throw new InvalidOperationException("Token refresh failed.");
            });

        _policy = Policy
            .Handle<HttpRequestException>()
            .OrResult<HttpResponseMessage>(r => r.StatusCode is HttpStatusCode.Unauthorized)
            .Retry(1, async (_, _) =>
            {
                var success = await httpMessageHandlerService.RefreshAsync();

                if (!success.GetValueOrDefault())
                    throw new InvalidOperationException("Token refresh failed.");
            });
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => await _asyncPolicy.ExecuteAsync(async () =>
        {
            var token = (await tokenStore.GetTokenAsync())?.Token ?? "";
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var result = await base.SendAsync(await CloneRequestIfNeededAsync(request), cancellationToken);

            if(result.IsSuccessStatusCode)
                await this.msg.RemoveWarningMessageAsync();

            return result;
        });

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    => _policy.Execute(() =>
    {
        var token = tokenStore.GetTokenAsync().Result?.Token ?? "";
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var result = base.Send(CloneRequestIfNeededAsync(request).GetAwaiter().GetResult(), cancellationToken);

        if (result.IsSuccessStatusCode)
            this.msg.RemoveWarningMessageAsync().Wait();

        return result;
    });

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