using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using ShiftSoftware.ShiftIdentity.Blazor.Services;

namespace ShiftSoftware.ShiftIdentity.Blazor.Components;

public class RedirectToIdentityServer : ComponentBase
{
    [Inject] NavigationManager NavManager { get; set; } = default!;
    [Inject] ShiftIdentityService ShiftIdentityService { get; set; } = default!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthenticationStateTask { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        var authenticationState = await AuthenticationStateTask;

        if (authenticationState?.User?.Identity is null || !authenticationState.User.Identity.IsAuthenticated)
        {
            var returnUrl = NavManager.ToBaseRelativePath(NavManager.Uri);

            await ShiftIdentityService.LoginAsync(returnUrl);
        }
    }

}