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

namespace ShiftIdentity.Blazor.Tests;

[Trait("Category", "Ui")]
public sealed class LoginComponentTests
{
    [Fact]
    public async Task Actual_login_and_mfa_forms_keep_challenge_anonymous_until_session()
    {
        using var context = Context(out var store, out var flow);
        var completed = 0;
        var cut = context.Render<LoginForm>(p => p.Add(x => x.AdmissionFlow, flow)
            .Add(x => x.SessionCompleted, _ => completed++));
        await Login(cut);
        Assert.Empty(store.Writes);
        Assert.Single(cut.FindComponents<MfaForm>());
        Assert.Equal("text", cut.Find("input").GetAttribute("type"));
        Assert.Equal("numeric", cut.Find("input").GetAttribute("inputmode"));
        Assert.Equal("one-time-code", cut.Find("input").GetAttribute("autocomplete"));
        Assert.Equal("http://localhost/", context.Services.GetRequiredService<NavigationManager>().Uri);
        cut.Find("input").Input("012345");
        await Submit(cut);
        Assert.Single(store.Writes);
        Assert.Equal(1, completed);
        Assert.Empty(cut.FindComponents<MfaForm>());
    }

    [Fact]
    public async Task Restricted_gate_renders_in_the_existing_form_and_can_restart()
    {
        using var context = Context(out var store, out var flow, AuthenticationStep.PasswordChange);
        var cut = context.Render<LoginForm>(p => p.Add(x => x.AdmissionFlow, flow));
        await Login(cut);
        Assert.Single(cut.FindAll("[data-testid=restricted-step]"));
        Assert.Empty(store.Writes);
        cut.FindAll("button").Single(x => x.TextContent.Contains("Start again")).Click();
        Assert.Null(flow.Pending);
        Assert.Single(cut.FindAll("form"));
    }

    [Fact]
    public async Task Invalid_code_keeps_mfa_form_and_does_not_create_a_session()
    {
        using var context = Context(out var store, out var flow, refuseMfa: true);
        var cut = context.Render<LoginForm>(p => p.Add(x => x.AdmissionFlow, flow));
        await Login(cut);
        cut.Find("input").Input("012345");
        await Submit(cut);
        Assert.Empty(store.Writes);
        Assert.Single(cut.FindComponents<MfaForm>());
        Assert.Equal("text", cut.Find("input").GetAttribute("type"));
        Assert.Equal("numeric", cut.Find("input").GetAttribute("inputmode"));
        Assert.Equal("one-time-code", cut.Find("input").GetAttribute("autocomplete"));
        Assert.Single(cut.FindAll("[data-testid=admission-error]"));
    }

    private static BunitContext Context(out RecordingStore store, out AuthenticationFlow flow,
        AuthenticationStep step = AuthenticationStep.ExistingMfa, bool refuseMfa = false)
    {
        var context = new BunitContext();
        var transport = new ScriptedHttp(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/mfa"))
            {
                var proof = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<CompleteMfaRequest>(request.Content!);
                Assert.Equal("012345", proof!.Code);
                return refuseMfa ? new AuthenticationRefused(AuthenticationFailure.InvalidProof) : AuthenticationFlowTests.Session();
            }
            var login = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<PasswordLoginRequest>(request.Content!);
            Assert.Equal("synthetic", login!.Username);
            Assert.Equal("password", login.Password);
            return AuthenticationFlowTests.Challenge(step);
        });
        store = new();
        var http = transport.Client();
        flow = new(http, store);
        context.Services.AddSingleton(http);
        context.Services.AddSingleton<IIdentityStore>(store);
        context.Services.AddShiftBlazor(options => options.ShiftConfiguration = config => config.BaseAddress = "https://identity.invalid");
        context.Services.AddShiftIdentityDashboardBlazor(_ => { });
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        context.AddAuthorization();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private static async Task Login(IRenderedComponent<LoginForm> cut)
    {
        cut.FindAll("input")[0].Input("synthetic");
        cut.FindAll("input")[1].Input("password");
        await Submit(cut);
    }

    private static Task Submit(IRenderedComponent<LoginForm> cut) => cut.InvokeAsync(() =>
        cut.FindComponent<EditForm>().Instance.OnValidSubmit.InvokeAsync(new EditContext(new object())));
}
