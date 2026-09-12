using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;

namespace ShiftSoftware.ShiftIdentity.Blazor.Providers;

public class ShiftIdentityAuthStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly IdentitySession tokenStore;

    public ShiftIdentityAuthStateProvider(IdentitySession tokenStore)
    {
        this.tokenStore = tokenStore;
        tokenStore.Changed += OnSessionChanged;
    }

    private void OnSessionChanged() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    public void Dispose() => tokenStore.Changed -= OnSessionChanged;
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = tokenStore.GetToken();

        var identity = new ClaimsIdentity();
        if (token is not null)
        {
            var handler = new JsonWebTokenHandler();
            var jsonToken = handler.ReadToken(token);
            var tokenS = jsonToken as JsonWebToken;

            identity = new ClaimsIdentity(tokenS?.Claims, "jwt");
        }

        var user = new ClaimsPrincipal(identity);
        var state = new AuthenticationState(user);

        if (tokenStore.NotifyOnAuthStateRead)
            NotifyAuthenticationStateChanged(Task.FromResult(state));

        return Task.FromResult(state);
    }
}
