using Microsoft.AspNetCore.Components;
using ShiftSoftware.ShiftIdentity.Blazor.Services;

namespace ShiftSoftware.ShiftIdentity.Blazor.Components;

[Route("/Auth/Token")]
public class Token : ComponentBase
{
    [Inject]
    ShiftIdentityService ShiftIdentityService { get; set; } = default!;

    [Parameter]
    [SupplyParameterFromQuery]
    public string? ReturnUrl { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public Guid AuthCode { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await ShiftIdentityService.GetTokenAsync(AuthCode, ReturnUrl);
    }
}