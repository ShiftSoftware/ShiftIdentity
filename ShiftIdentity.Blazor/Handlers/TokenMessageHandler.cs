using ShiftSoftware.ShiftIdentity.Core.Models;
using System.Net.Http.Headers;

namespace ShiftSoftware.ShiftIdentity.Blazor.Handlers;

public class TokenMessageHandler : DelegatingHandler
{
    private readonly IIdentityStore tokenStore;

    public TokenMessageHandler(IIdentityStore tokenStore)
    {
        //add this to solve "The inner handler has not been assigned"
        InnerHandler = new HttpClientHandler();

        this.tokenStore = tokenStore;
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = tokenStore.GetTokenAsync().Result.Token;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return base.Send(request, cancellationToken);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = tokenStore.GetTokenAsync().Result.Token ?? "";
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return base.SendAsync(request, cancellationToken);
    }
}
