using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShiftSoftware.ShiftIdentity.Core;


namespace ShiftSoftware.ShiftIdentity.Blazor.Components;

public class RedirectToLogin : ComponentBase
{
    [Inject] IServiceProvider ServiceProvider { get; set; } = default!;
    [Inject] NavigationManager NavManager { get; set; } = default!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        var options = ServiceProvider.GetService<ShiftIdentityBlazorOptions>();

        var authState = AuthenticationStateTask != null ? await AuthenticationStateTask : null;

        if (authState?.User.Identity is null || !authState.User.Identity.IsAuthenticated)
        {
            var returnUrl = NavManager.ToBaseRelativePath(NavManager.Uri);
            var queryStrings = new Dictionary<string, object?>();
            const string url = $"/{Constants.IdentityRoutePreifix}/login";

            if (!string.IsNullOrWhiteSpace(returnUrl))
                queryStrings.Add(Constants.ReturnUrlParameter, returnUrl);

            var uri = NavManager.GetUriWithQueryParameters(url, queryStrings);
            NavManager.NavigateTo(uri);
        }
    }
}
