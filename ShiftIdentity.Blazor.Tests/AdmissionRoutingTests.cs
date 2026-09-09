using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftIdentity.Blazor;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.Auth;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.UserManager;
using Xunit;
using ShiftSoftware.TypeAuth.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Core;
using System.Security.Claims;

namespace ShiftIdentity.Blazor.Tests;

[Trait("Category", "Ui")]
public sealed class AdmissionRoutingTests
{
    [Fact]
    public async Task Background_authentication_rerender_keeps_recovery_entry_but_target_change_clears_it()
    {
        using var context = new BunitContext();
        var store = new RecordingStore(); await store.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var handler = new AccountResponses(); var http = new HttpClient(handler) { BaseAddress = new Uri("https://identity.invalid/") };
        var ui = new AdmissionUiContext(new AuthenticationFlow(http, store), store, http);
        context.Services.AddShiftBlazor(o => o.ShiftConfiguration = c => c.BaseAddress = "https://identity.invalid/");
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = context.Render<AdmissionAccountPanel>(p => p.Add(x => x.Context, ui).Add(x => x.Administrator, true).Add(x => x.UserKey, "42"));
        cut.Find("input").Input("Synthetic independent verification");
        cut.Render();
        Assert.Equal("Synthetic independent verification", cut.Find("input").GetAttribute("value"));
        Assert.Equal(2, handler.Reads);
        await cut.InvokeAsync(() => cut.FindComponent<EditForm>().Instance.OnValidSubmit.InvokeAsync(new EditContext(new object())));
        Assert.Contains("Recovery required", cut.Markup);
        cut.Render();
        Assert.Single(cut.FindAll("[data-testid=recovery-code]"));
        cut.Render(p => p.Add(x => x.Context, ui).Add(x => x.Administrator, true).Add(x => x.UserKey, "43"));
        Assert.NotEqual("Synthetic independent verification", cut.Find("input").GetAttribute("value"));
        Assert.Empty(cut.FindAll("[data-testid=recovery-code]"));
    }

    private sealed class AccountResponses : HttpMessageHandler
    {
        public int Reads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post) return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = System.Net.Http.Json.JsonContent.Create<AuthOutcome>(new MfaRecoveryCodeIssued("Synthetic code", DateTimeOffset.UtcNow.AddMinutes(15))) });
            Reads++;
            var id = request.RequestUri!.Segments.Last() == "account" ? 17 : long.Parse(request.RequestUri.Segments.Last());
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = System.Net.Http.Json.JsonContent.Create(new AdmissionAccount(id, "synthetic", "Synthetic", true, false, id == 17)) });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recovery_navigation_uses_its_dedicated_Boolean_permission(bool permitted)
    {
        using var context = new BunitContext();
        var store = new RecordingStore(); var http = new HttpClient();
        var ui = new AdmissionUiContext(new AuthenticationFlow(http, store), store, http);
        context.Services.AddShiftBlazor(o => o.ShiftConfiguration = c => c.BaseAddress = "https://identity.invalid/");
        context.Services.AddShiftIdentityDashboardBlazor(_ => { });
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        var auth = context.AddAuthorization(); auth.SetAuthorized("synthetic");
        if (permitted) auth.SetClaims(new Claim(ShiftSoftware.TypeAuth.Core.TypeAuthClaimTypes.AccessTree,
            "{\"ShiftIdentityActions\":{\"ManageMfaRecovery\":[\"m\"]}}"));
        context.Services.AddTypeAuth(o => o.AddActionTree<ShiftIdentityActions>());
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = context.Render<CascadingValue<AdmissionUiContext>>(p => p.Add(x => x.Value, ui)
            .AddChildContent<ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared.NavMenu>());
        Assert.Equal(permitted, cut.FindAll("a[href='Identity/UserList']").Count == 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Routed_login_resolves_host_context_and_only_navigates_after_complete_session(bool challenge)
    {
        using var context = new BunitContext();
        var store = new RecordingStore();
        var calls = new List<string>();
        var transport = new ScriptedHttp(request => { calls.Add(request.RequestUri!.AbsolutePath); return Task.FromResult<AuthOutcome>(challenge ? AuthenticationFlowTests.Challenge() : AuthenticationFlowTests.Session()); });
        var http = transport.Client(); var flow = new AuthenticationFlow(http, store);
        var ui = new AdmissionUiContext(flow, store, http);
        context.Services.AddSingleton(http); context.Services.AddSingleton<IIdentityStore>(store);
        context.Services.AddShiftBlazor(o => o.ShiftConfiguration = c => c.BaseAddress = "https://identity.invalid/");
        context.Services.AddShiftIdentityDashboardBlazor(_ => { });
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        context.AddAuthorization(); context.JSInterop.Mode = JSRuntimeMode.Loose;
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Identity/login");
        var cut = context.Render<CascadingValue<AdmissionUiContext>>(p => p.Add(x => x.Value, ui).AddChildContent<LoginForm>());
        cut.FindAll("input")[0].Input("synthetic"); cut.FindAll("input")[1].Input("password");
        await cut.InvokeAsync(() => cut.FindComponent<EditForm>().Instance.OnValidSubmit.InvokeAsync(new EditContext(new object())));
        Assert.Equal("/api/identity/v2/login", Assert.Single(calls));
        if (challenge)
        {
            Assert.Empty(store.Writes);
            Assert.EndsWith("Identity/login", navigation.Uri);
            Assert.Single(cut.FindComponents<MfaForm>());
        }
        else
        {
            Assert.Single(store.Writes);
            Assert.Equal(navigation.BaseUri, navigation.Uri);
        }
    }
}
