using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
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
/// The login screen's Microsoft button: shown only when the host turned it on, it sends the browser to Microsoft with
/// the verifier kept in session storage, and on the return completes with that verifier and the handle in the fragment.
/// </summary>
[Trait("Category", "Ui")]
public sealed class MicrosoftLoginComponentTests
{
    private const string Pending = "shift-identity-microsoft";
    private const string Authorize = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id=x";

    [Fact]
    public void The_button_shows_only_when_the_host_turned_Microsoft_on()
    {
        using (var off = Context(false, out _, out _, out var flow))
            Assert.Empty(off.Render<LoginForm>(p => p.Add(x => x.AdmissionFlow, flow)).FindAll("[data-testid=login-microsoft]"));
        using var on = Context(true, out _, out _, out var enabled);
        Assert.Single(on.Render<LoginForm>(p => p.Add(x => x.AdmissionFlow, enabled)).FindAll("[data-testid=login-microsoft]"));
    }

    [Fact]
    public async Task The_button_keeps_the_verifier_and_return_path_then_leaves_for_Microsoft()
    {
        using var context = Context(true, out var http, out _, out var flow);
        var store = context.JSInterop.SetupVoid("sessionStorage.setItem", _ => true);
        store.SetVoidResult();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/Identity/login?ReturnUrl=%2Forders");
        var cut = context.Render<LoginForm>(p => p.Add(x => x.AdmissionFlow, flow));
        await cut.Find("[data-testid=login-microsoft]").ClickAsync(new());
        Assert.Equal(Authorize, navigation.Uri);
        var saved = store.Invocations.Single();
        Assert.Equal(Pending, saved.Arguments[0]);
        var pending = JsonDocument.Parse((string)saved.Arguments[1]!).RootElement;
        Assert.Equal("/orders", pending.GetProperty("ReturnUrl").GetString());
        var start = JsonSerializer.Deserialize<StartProviderSignInRequest>(http.Requests.Single().Body, ScriptedHttp.Json)!;
        var verifier = pending.GetProperty("Verifier").GetString()!;
        Assert.Equal(start.CodeChallenge, Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
    }

    [Fact]
    public void The_return_completes_with_the_kept_verifier_stores_the_session_and_clears_the_handle()
    {
        using var context = Context(true, out var http, out var store, out var flow);
        context.JSInterop.Setup<string?>("sessionStorage.getItem", Pending)
            .SetResult(JsonSerializer.Serialize(new { Verifier = new string('v', 43), ReturnUrl = "/orders" }));
        var removed = context.JSInterop.SetupVoid("sessionStorage.removeItem", Pending);
        removed.SetVoidResult();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/Identity/login#microsoft=handle.value");
        var cut = context.Render<LoginForm>(p => p.Add(x => x.AdmissionFlow, flow));
        cut.WaitForAssertion(() => Assert.Single(store.Writes));
        var complete = JsonSerializer.Deserialize<CompleteProviderSignInRequest>(http.Requests.Single().Body, ScriptedHttp.Json)!;
        Assert.Equal(("handle.value", new string('v', 43)), (complete.Handle, complete.CodeVerifier));
        Assert.Single(removed.Invocations);
        Assert.DoesNotContain("handle.value", navigation.Uri);
    }

    [Fact]
    public void A_returned_failure_shows_its_own_message_without_a_request()
    {
        using var context = Context(true, out var http, out var store, out var flow);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/Identity/login#microsoft-error=ProviderAccountNotFound");
        var cut = context.Render<LoginForm>(p => p.Add(x => x.AdmissionFlow, flow));
        Assert.Contains("No account uses this Microsoft account's email.", cut.Find("[data-testid=login-failure-message]").TextContent);
        Assert.Empty(http.Requests);
        Assert.Empty(store.Writes);
    }

    private static BunitContext Context(bool microsoft, out ScriptedHttp transport, out RecordingStore store, out AuthenticationFlow flow)
    {
        var context = new BunitContext();
        transport = new ScriptedHttp(request => Task.FromResult<AuthOutcome>(request.RequestUri!.AbsolutePath.EndsWith("/start")
            ? new ProviderRedirect(Authorize) : AuthenticationFlowTests.Session())) { Providers = new(microsoft) };
        store = new();
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
