using System.Security.Claims;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.User;
using ShiftSoftware.TypeAuth.Blazor.Extensions;
using ShiftSoftware.TypeAuth.Core;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// The user import page must show the button that opens the file picker. MudBlazor 9 removed MudFileUpload's
/// ActivatorContent, so content written there was passed on as an unknown attribute and the page showed no button.
/// </summary>
[Trait("Category", "Ui")]
public sealed class UserImportFormTests
{
    [Fact]
    public async Task The_page_shows_the_button_that_opens_the_file_picker()
    {
        await using var context = new BunitContext();
        context.Services.AddSingleton(new HttpClient(new CompanyCalendarFormTransport()) { BaseAddress = new Uri("https://identity.invalid/") });
        context.Services.AddShiftBlazor(o => o.ShiftConfiguration = c => c.BaseAddress = "https://identity.invalid/");
        context.Services.AddShiftIdentityDashboardBlazor(_ => { });
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        var auth = context.AddAuthorization(); auth.SetAuthorized("synthetic");
        auth.SetClaims(new Claim(TypeAuthClaimTypes.AccessTree, "{\"ShiftIdentityActions\":{\"Users\":[\"r\",\"w\"]}}"));
        context.Services.AddTypeAuth(o => o.AddActionTree<ShiftIdentityActions>());
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = context.Render<UserImportForm>();

        var button = cut.FindAll("button").SingleOrDefault(b => b.TextContent.Contains("Select CSV File"));
        Assert.NotNull(button);
        Assert.DoesNotContain("childcontent", cut.Markup, StringComparison.OrdinalIgnoreCase);

        await button.ClickAsync(new());
        Assert.Contains(context.JSInterop.Invocations, invocation => invocation.Identifier == "mudFileUpload.openFilePicker");
    }
}
