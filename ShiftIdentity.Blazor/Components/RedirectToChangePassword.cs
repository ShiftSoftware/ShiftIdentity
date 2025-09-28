using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Blazor.Components;

public class RedirectToChangePassword : ComponentBase
{
    [Inject] IServiceProvider ServiceProvider { get; set; } = default!;
    [Inject] NavigationManager NavManager { get; set; } = default!;


    ShiftIdentityBlazorOptions? Options { get; set; }
    AuthenticationStateProvider? AuthStateProvider { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Options = ServiceProvider.GetService<ShiftIdentityBlazorOptions>();
        AuthStateProvider = ServiceProvider.GetService<AuthenticationStateProvider>();

        if (NavManager.Uri.Contains($"{ShiftIdentity.Core.Constants.IdentityRoutePreifix}/ChangePasswordForm"))
            return;

        if (this.AuthStateProvider is null)
            return;

        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        if (authState is null || !(authState.User?.Identity?.IsAuthenticated ?? false))
            return;

        var requirePasswordChangeClaim = authState.User?.Claims?.FirstOrDefault(c => c.Type == ShiftIdentityClaims.RequirePasswordChange)?.Value;
        var parseSuccess = bool.TryParse(requirePasswordChangeClaim, out bool requirePaswordChange);

        if (parseSuccess && requirePaswordChange)
        {
            if (Options is null)
                return;

            //Redirect to change password form
            var returnUrl = NavManager.ToBaseRelativePath(NavManager.Uri);

            var queryStrings = new Dictionary<string, object?>
            {
                { "Enforce", true }
            };

            //Add return-url to login page
            if (!string.IsNullOrWhiteSpace(returnUrl))
                queryStrings.Add("ReturnUrl", returnUrl);

            var url = Options.FrontEndBaseUrl.EndsWith('/') ? Options.FrontEndBaseUrl : Options.FrontEndBaseUrl + '/';
            var uri = NavManager.GetUriWithQueryParameters(url + $"{ShiftIdentity.Core.Constants.IdentityRoutePreifix}/ChangePasswordForm",
                queryStrings);
            NavManager.NavigateTo(uri);
        }
    }
}
