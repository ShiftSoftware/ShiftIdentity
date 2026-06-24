using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Enums;

namespace ShiftSoftware.ShiftIdentity.Blazor.Components;

/// <summary>
/// Inspects the authenticated user's <see cref="ShiftIdentityClaims.TokenPurpose"/> claim and,
/// when the token represents an in-progress auth flow, redirects to the matching form.
/// Replaces the former per-flow components (RedirectToChangePassword, RedirectToTwoFactor,
/// RedirectToTwoFactorEnrollment).
/// </summary>
public class RedirectToAuthFlow : ComponentBase
{
    [Inject] IServiceProvider ServiceProvider { get; set; } = default!;
    [Inject] NavigationManager NavManager { get; set; } = default!;

    ShiftIdentityBlazorOptions? Options { get; set; }
    AuthenticationStateProvider? AuthStateProvider { get; set; }

    private Dictionary<AuthPurpose, RedirectSetting> RedirectSettings { get; set; } = new()
    {
        { AuthPurpose.ChangePassword, new RedirectSetting($"{Constants.IdentityRoutePreifix}/ChangePasswordForm", true)},
        { AuthPurpose.Mfa, new RedirectSetting($"{Constants.IdentityRoutePreifix}/MfaForm", false)},
        { AuthPurpose.MfaEnrollment, new RedirectSetting($"{Constants.IdentityRoutePreifix}/TotpEnrollmentForm", true)},
    };

    private record RedirectSetting(string Url, bool Force);

    protected override async Task OnInitializedAsync()
    {
        Options = ServiceProvider.GetService<ShiftIdentityBlazorOptions>();
        AuthStateProvider = ServiceProvider.GetService<AuthenticationStateProvider>();

        if (Options is null || AuthStateProvider is null)
            return;

        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        if (authState is null || !(authState.User?.Identity?.IsAuthenticated ?? false))
            return;

        var purposeClaim = authState.User?.Claims?.FirstOrDefault(c => c.Type == ShiftIdentityClaims.TokenPurpose)?.Value;

        if (!Enum.TryParse(purposeClaim, out AuthPurpose flow) || flow == AuthPurpose.None)
            return;

        var redirectSettings = RedirectSettings[flow];

        // Already on the target form — nothing to do (avoids a redirect loop).
        if (NavManager.Uri.Contains(redirectSettings.Url))
            return;

        var queryStrings = new Dictionary<string, object?>();

        if (redirectSettings.Force)
            queryStrings.Add("Enforce", true);

        // Add return-url so the user lands back where they were after completing the flow.
        var returnUrl = NavManager.ToBaseRelativePath(NavManager.Uri);
        if (!string.IsNullOrWhiteSpace(returnUrl))
            queryStrings.Add("ReturnUrl", returnUrl);

        var url = Options.FrontEndBaseUrl.EndsWith('/') ? Options.FrontEndBaseUrl : Options.FrontEndBaseUrl + '/';
        var uri = NavManager.GetUriWithQueryParameters(url + redirectSettings.Url, queryStrings);
        NavManager.NavigateTo(uri);
    }
}
