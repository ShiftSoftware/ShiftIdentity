using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using ShiftSoftware.ShiftIdentity.Blazor;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.UserManager;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services;

internal sealed class AdministratorConfirmation(IdentitySession session, IServiceProvider services) : IAdministratorConfirmation
{
    public async Task<string?> ConfirmAsync(string currentAccess, CancellationToken cancellationToken)
    {
        var dialogs = services.GetRequiredService<IDialogService>();
        var navigation = services.GetRequiredService<NavigationManager>();
        // A separate flow keeps the original form's pending HTTP request and staged flow intact.
        var flow = new AuthenticationFlow(services.GetRequiredService<StagedAuthorityHttpClient>(), session);
        var dialog = await dialogs.ShowAsync<AdministratorConfirmationDialog>("Confirm your identity",
            new DialogParameters<AdministratorConfirmationDialog>
            {
                { x => x.Flow, flow }, { x => x.CurrentAccess, currentAccess }
            }, new DialogOptions { BackdropClick = false, CloseOnEscapeKey = false, FullWidth = true, MaxWidth = MaxWidth.Small });
        void Cancel() { _ = flow.CancelAsync(); dialog.Close(DialogResult.Cancel()); }
        void Navigated(object? sender, LocationChangedEventArgs args) => Cancel();
        navigation.LocationChanged += Navigated;
        using var cancellation = cancellationToken.Register(Cancel);
        try
        {
            var result = await dialog.Result;
            return result is { Canceled: false, Data: string access } && !cancellationToken.IsCancellationRequested ? access : null;
        }
        finally
        {
            navigation.LocationChanged -= Navigated;
            await flow.CancelAsync();
        }
    }
}
