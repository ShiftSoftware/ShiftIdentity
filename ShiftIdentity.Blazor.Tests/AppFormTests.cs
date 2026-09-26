using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftBlazor.Components;
using ShiftSoftware.ShiftBlazor.Enums;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.App;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.App;
using ShiftSoftware.TypeAuth.Blazor.Extensions;
using ShiftSoftware.TypeAuth.Core;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// The App form through the real ShiftEntityForm save path. A new or changed RedirectUri that ends in "/" is refused,
/// but a row that already has one saves unchanged, because the app-code binding hashes the stored value
/// (FE-2026-09-25-02).
/// </summary>
[Trait("Category", "Ui")]
public sealed class AppFormTests
{
    private const string Message = "The Redirect URI cannot end with / or /Auth/Token";

    [Fact]
    public async Task A_row_that_already_ends_in_a_slash_saves_unchanged()
    {
        await using var context = Create(out var transport);
        transport.Existing = App("http://localhost:5015/");
        var cut = await OpenForEditAsync(context);

        Assert.Contains("http://localhost:5015/Auth/Token", cut.Find(".mud-alert").TextContent);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Single(transport.Saves));
        Assert.Equal("http://localhost:5015/", JsonSerializer.Deserialize<AppDTO>(transport.Saves[0], Web)!.RedirectUri);
    }

    [Fact]
    public async Task Changing_a_row_to_a_value_ending_in_a_slash_shows_the_message_and_is_not_saved()
    {
        await using var context = Create(out var transport);
        transport.Existing = App("https://app.invalid");
        var cut = await OpenForEditAsync(context);
        var form = cut.FindComponent<ShiftEntityForm<AppDTO>>();
        await cut.InvokeAsync(() => form.Instance.Value.RedirectUri = "https://app.invalid/");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(Message, cut.Markup));
        Assert.Empty(transport.Saves);
    }

    [Fact]
    public async Task A_new_app_ending_in_a_slash_shows_the_message_and_is_not_saved()
    {
        await using var context = Create(out var transport);
        var cut = context.Render<AppForm>();
        var form = cut.FindComponent<ShiftEntityForm<AppDTO>>();
        await cut.InvokeAsync(() =>
        {
            form.Instance.Value.DisplayName = "Synthetic app";
            form.Instance.Value.AppId = "synthetic-app";
            form.Instance.Value.RedirectUri = "https://app.invalid/";
        });

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(Message, cut.Markup));
        Assert.Empty(transport.Saves);
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static AppDTO App(string redirectUri) => new()
    {
        ID = "42", DisplayName = "Synthetic app", AppId = "synthetic-app", RedirectUri = redirectUri
    };

    private static async Task<IRenderedComponent<AppForm>> OpenForEditAsync(BunitContext context)
    {
        var cut = context.Render<AppForm>(p => p.Add(x => x.Key, "42"));
        var form = cut.FindComponent<ShiftEntityForm<AppDTO>>();
        cut.WaitForState(() => form.Instance.Value.ID == "42" && form.Instance.TaskInProgress == FormTasks.None);
        await cut.InvokeAsync(form.Instance.EditItem);
        cut.WaitForAssertion(() => Assert.Equal(FormModes.Edit, form.Instance.Mode));
        return cut;
    }

    private static BunitContext Create(out AppFormTransport transport)
    {
        var context = new BunitContext();
        transport = new();
        context.Services.AddSingleton(new HttpClient(transport) { BaseAddress = new Uri("https://identity.invalid/") });
        context.Services.AddShiftBlazor(o => o.ShiftConfiguration = c => c.BaseAddress = "https://identity.invalid/");
        context.Services.AddShiftIdentityDashboardBlazor(_ => { });
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        var auth = context.AddAuthorization(); auth.SetAuthorized("synthetic");
        auth.SetClaims(new Claim(TypeAuthClaimTypes.AccessTree, "{\"ShiftIdentityActions\":{\"Apps\":[\"r\",\"w\"]}}"));
        context.Services.AddTypeAuth(o => o.AddActionTree<ShiftIdentityActions>());
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    /// <summary>Records saves, serves one existing app by key, and answers every other request with an empty OData page.</summary>
    private sealed class AppFormTransport : HttpMessageHandler
    {
        public List<string> Saves { get; } = [];

        public AppDTO? Existing { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put)
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Saves.Add(body);
                var saved = JsonSerializer.Deserialize<AppDTO>(body, Web)!;
                saved.ID ??= "42";
                return new(request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new ShiftEntityResponse<AppDTO>(saved))
                };
            }
            if (request.Method == HttpMethod.Get && Existing is not null && path == $"/IdentityApp/{Existing.ID}")
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new ShiftEntityResponse<AppDTO>(Existing)) };
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"value\":[]}", System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
