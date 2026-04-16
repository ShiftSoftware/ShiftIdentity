using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using System.Text.Json;

namespace ShiftSoftware.ShiftIdentity.Blazor.Components;

[Route("/Auth/Token")]
public class Token : ComponentBase
{
    [Inject]
    ShiftIdentityService ShiftIdentityService { get; set; } = default!;

    [Inject]
    IServiceProvider ServiceProvider { get; set; } = default!;

    [Inject]
    NavigationManager NavManager { get; set; } = default!;

    [Parameter]
    [SupplyParameterFromQuery]
    public string? ReturnUrl { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public Guid AuthCode { get; set; }

    protected override async Task OnInitializedAsync()
    {
        // SSR with cookie auth: use ICookieTokenService to exchange code and set cookie
        var cookieTokenService = ServiceProvider.GetService<ICookieTokenService>();

        if (cookieTokenService != null)
        {
            // Read code verifier from the HttpOnly cookie (set during the login redirect)
            var codeVerifier = cookieTokenService.GetCodeVerifierFromCookie();

            if (codeVerifier != null)
            {
                var identityOptions = ServiceProvider.GetRequiredService<ShiftIdentityBlazorOptions>();
                var success = await cookieTokenService.ExchangeCodeAndSignInAsync(
                    identityOptions.AppId, AuthCode, codeVerifier);

                if (success)
                {
                    NavManager.NavigateTo("/" + (ReturnUrl ?? "").TrimStart('/'), forceLoad: true);
                    return;
                }
            }

            NavManager.NavigateTo("/", forceLoad: true);
            return;
        }

        // WASM/standalone: use the original ShiftIdentityService flow
        await ShiftIdentityService.GetTokenAsync(AuthCode, ReturnUrl);
    }
}
