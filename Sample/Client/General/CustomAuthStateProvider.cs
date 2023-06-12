using Microsoft.AspNetCore.Components.Authorization;
using Sample.Client.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;

namespace Sample.Client.General;

public class CustomAuthStateProvider : AuthenticationStateProvider
{
    private readonly StorageService storageService;

    public CustomAuthStateProvider(StorageService storageService)
    {
        this.storageService = storageService;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await storageService.LoadTokenAsync();

        var identity = new ClaimsIdentity();
        if (token is not null)
        {
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(token.Token);
            var tokenS = jsonToken as JwtSecurityToken;

            identity = new ClaimsIdentity(tokenS?.Claims, "jwt");
        }

        var user = new ClaimsPrincipal(identity);
        var state = new AuthenticationState(user);

        NotifyAuthenticationStateChanged(Task.FromResult(state));

        return state;
    }
}
