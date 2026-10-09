using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ShiftIdentity.DevHost.Client;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftBlazor.Services;
using ShiftSoftware.ShiftIdentity.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Blazor.Handlers;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;
using ShiftSoftware.TypeAuth.Blazor.Extensions;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// The DevHost serves this client and the identity API from one origin: loopback, or the LAN address it was asked to
// bind, so that a phone can open it. Every address is taken from the origin the browser loaded the client from.
var origin = builder.HostEnvironment.BaseAddress;
var api = origin + "api/";

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient(sp.GetRequiredService<TokenMessageHandlerWithAutoRefresh>())
{
    BaseAddress = new Uri(api)
});

builder.Services.AddShiftBlazor(config =>
{
    config.ShiftConfiguration = options =>
    {
        options.BaseAddress = api;
        options.ExternalAddresses = new Dictionary<string, string?> { ["ShiftIdentityApi"] = api };
        options.UserListEndpoint = api + "IdentityPublicUser";
        // The languages the framework translates, so each page can be checked in them.
        options.AddLanguage("en-US", "English")
               .AddLanguage("ar-IQ", "Arabic", true)
               .AddLanguage("ku-IQ", "Kurdish", true)
               .AddLanguage("ru-RU", "Russian");
    };
});

// The same registration as an identity host's own client: the dashboard login and account screens use the
// authority's flows (StagedAuthority), and the session renews through the deployed refresh route.
builder.Services.AddShiftIdentity("identity-devhost", api, origin.TrimEnd('/'));
builder.Services.AddShiftIdentityDashboardBlazor(x =>
{
    x.ShiftIdentityHostingType = ShiftIdentityHostingTypes.Internal;
    x.Title = "Identity DevHost";
    x.StagedAuthority = true;
    x.DynamicTypeAuthActionExpander = () => Task.CompletedTask;
});

builder.Services.AddTypeAuth(x => x.AddActionTree<ShiftIdentityActions>());

var host = builder.Build();
// The language saved in this browser, applied as a host's own client applies it.
var culture = host.Services.GetRequiredService<SettingManager>().GetCulture();
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;
await host.RefreshTokenAsync(50);
await host.RunAsync();
