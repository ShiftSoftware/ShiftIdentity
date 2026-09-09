using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using ShiftSoftware.ShiftIdentity.Core.Localization;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared;

/// <summary>A regular text field whose password visibility never changes its value.</summary>
public class PasswordField : MudTextField<string>
{
    [Inject] public ShiftIdentityLocalizer Localizer { get; set; } = null!;
    private bool visible;

    protected override void OnParametersSet()
    {
        UpdateVisibility();
        base.OnParametersSet();
    }

    private void UpdateVisibility()
    {
        InputType = visible ? InputType.Text : InputType.Password;
        Adornment = Disabled ? Adornment.None : Adornment.End;
        AdornmentIcon = visible ? Icons.Material.Filled.VisibilityOff : Icons.Material.Filled.Visibility;
        AdornmentAriaLabel = $"{Localizer[visible ? "Hide" : "Show"]} {Label ?? Localizer["Password"]}";
        OnAdornmentClick = EventCallback.Factory.Create<MouseEventArgs>(this, () =>
        {
            if (Disabled) return;
            visible = !visible;
            UpdateVisibility();
        });
    }
}
