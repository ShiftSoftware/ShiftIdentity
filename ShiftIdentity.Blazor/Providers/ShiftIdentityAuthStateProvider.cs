using Microsoft.AspNetCore.Components.Authorization;
using ShiftSoftware.ShiftIdentity.Core.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ShiftSoftware.ShiftIdentity.Blazor.Providers;

public class ShiftIdentityAuthStateProvider : AuthenticationStateProvider
{
    private readonly IIdentityStore tokenStore;

    public ShiftIdentityAuthStateProvider(IIdentityStore tokenStore)
    {
        this.tokenStore = tokenStore;
    }
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = tokenStore.GetToken();

        var identity = new ClaimsIdentity();
        if (token is not null)
        {
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(token);
            var tokenS = jsonToken as JwtSecurityToken;

            identity = new ClaimsIdentity(tokenS?.Claims, "jwt");
        }

        var user = new ClaimsPrincipal(identity);
        var state = new AuthenticationState(user);

        NotifyAuthenticationStateChanged(Task.FromResult(state));

        return Task.FromResult(state);
    }
}
