using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.Auth;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// The login screen for accounts held to a trusted Microsoft tenant: an email on a held domain is told to sign in with
/// Microsoft before any password is sent, a username is answered by the server, and Microsoft's refusals of another
/// tenant or account explain what to use instead.
/// </summary>
[Trait("Category", "Ui")]
public sealed class TrustedTenantLoginComponentTests
{
    [Fact]
    public async Task An_email_on_a_held_domain_is_told_to_sign_in_with_Microsoft_and_no_password_is_sent()
    {
        using var context = Context(new(true, false, ["contoso.example"]), out var http, out var flow);
        var cut = context.Render<LoginForm>(p => p.Add(x => x.AdmissionFlow, flow));
        cut.WaitForElement("[data-testid=login-microsoft]");
        cut.FindAll("input")[0].Input("Person@Contoso.Example");
        cut.FindAll("input")[1].Input("password");
        await FormSubmission.SubmitAsync(cut);
        cut.WaitForAssertion(() => Assert.Contains("Your account signs in with your organization's Microsoft account.",
            cut.Find("[data-testid=login-failure-message]").TextContent));
        Assert.Contains("Choose Sign in with Microsoft.", cut.Find("[data-testid=login-failure-message]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=login-failure-reference]"));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_username_goes_to_the_server_which_answers_after_the_password()
    {
        using var context = Context(new(true, false, ["contoso.example"]), out var http, out var flow,
            new AuthenticationRefused(AuthenticationFailure.PasswordSignInDisabled));
        var cut = context.Render<LoginForm>(p => p.Add(x => x.AdmissionFlow, flow));
        cut.FindAll("input")[0].Input("person");
        cut.FindAll("input")[1].Input("password");
        await FormSubmission.SubmitAsync(cut);
        cut.WaitForAssertion(() => Assert.Contains("Choose Sign in with Microsoft.", cut.Find("[data-testid=login-failure-message]").TextContent));
        Assert.Single(http.Requests);
    }

    [Theory]
    [InlineData("microsoft")]
    [InlineData("google")]
    public void Another_tenant_or_provider_is_told_to_use_the_organization_s_Microsoft_account(string provider)
    {
        using var context = Context(new(true, true, ["contoso.example"]), out var http, out var flow);
        context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo($"/Identity/login#{provider}-error=ProviderNotAllowedForAccount");
        var cut = context.Render<LoginForm>(p => p.Add(x => x.AdmissionFlow, flow));
        var message = cut.Find("[data-testid=login-failure-message]").TextContent;
        Assert.Contains("Your account signs in with your organization's Microsoft account.", message);
        Assert.Contains("not a personal or other account", message);
        Assert.Empty(cut.FindAll("[data-testid=login-failure-reference]"));
        Assert.Empty(http.Requests);
    }

    private static BunitContext Context(SignInProviders providers, out ScriptedHttp transport, out AuthenticationFlow flow,
        AuthOutcome? answer = null)
    {
        var context = new BunitContext();
        transport = new ScriptedHttp(_ => Task.FromResult(answer ?? AuthenticationFlowTests.Session())) { Providers = providers };
        var store = new RecordingStore();
        var http = transport.Client();
        flow = new(http, store.Session);
        context.Services.AddSingleton(http);
        context.Services.AddSingleton(store); context.Services.AddSingleton(store.Session);
        context.Services.AddShiftBlazor(options => options.ShiftConfiguration = config => config.BaseAddress = "https://identity.invalid");
        context.Services.AddShiftIdentityDashboardBlazor(_ => { });
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        context.AddAuthorization();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }
}
