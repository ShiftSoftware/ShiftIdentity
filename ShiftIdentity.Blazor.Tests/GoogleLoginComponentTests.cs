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
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.UserManager;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// The login screen's Google button beside Microsoft's, Google's return and failures, and the linked accounts panel:
/// with only Microsoft involved it reads as before, with both it names each row's provider.
/// </summary>
[Trait("Category", "Ui")]
public sealed class GoogleLoginComponentTests
{
    private const string Pending = "shift-identity-google";
    private const string Authorize = "https://accounts.google.com/o/oauth2/v2/auth?client_id=x";

    [Fact]
    public void The_buttons_show_for_the_providers_the_host_turned_on_Microsoft_first()
    {
        using (var google = Context(new(false, true), out _, out _, out var flow))
        {
            var only = google.Render<LoginForm>(p => p.Add(x => x.AdmissionFlow, flow));
            Assert.Single(only.FindAll("[data-testid=login-google]"));
            Assert.Empty(only.FindAll("[data-testid=login-microsoft]"));
        }
        using var both = Context(new(true, true), out _, out _, out var enabled);
        var cut = both.Render<LoginForm>(p => p.Add(x => x.AdmissionFlow, enabled));
        Assert.Equal(["login-microsoft", "login-google"], cut.FindAll("button.auth-provider").Select(x => x.GetAttribute("data-testid")));
        Assert.Single(cut.FindAll(".auth-divider"));
        Assert.Contains("Sign in with Google", cut.Find("[data-testid=login-google]").TextContent);
    }

    [Fact]
    public async Task The_Google_button_keeps_its_own_pending_state_then_leaves_for_Google()
    {
        using var context = Context(new(true, true), out var http, out _, out var flow);
        var store = context.JSInterop.SetupVoid("sessionStorage.setItem", _ => true);
        store.SetVoidResult();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var cut = context.Render<LoginForm>(p => p.Add(x => x.AdmissionFlow, flow));
        await cut.Find("[data-testid=login-google]").ClickAsync(new());
        Assert.Equal(Authorize, navigation.Uri);
        Assert.Equal(Pending, store.Invocations.Single().Arguments[0]);
        Assert.Single(http.Requests);
        Assert.EndsWith("/providers/google/start", http.Paths.Single());
    }

    [Fact]
    public async Task A_Google_start_that_points_anywhere_but_Google_is_refused()
    {
        using var context = Context(new(false, true), out _, out _, out var flow, authorize: "https://login.microsoftonline.com/common/oauth2/v2.0/authorize");
        var (outcome, verifier) = await flow.StartProviderAsync(SignInProvider.Google);
        Assert.IsType<AuthenticationRefused>(outcome);
        Assert.Null(verifier);
    }

    [Fact]
    public void The_Google_return_completes_at_Google_s_route_with_the_kept_verifier()
    {
        using var context = Context(new(false, true), out var http, out var store, out var flow);
        context.JSInterop.Setup<string?>("sessionStorage.getItem", Pending)
            .SetResult(JsonSerializer.Serialize(new { Verifier = new string('v', 43), ReturnUrl = "/orders" }));
        context.JSInterop.SetupVoid("sessionStorage.removeItem", Pending).SetVoidResult();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/Identity/login#google=handle.value");
        context.Render<LoginForm>(p => p.Add(x => x.AdmissionFlow, flow)).WaitForAssertion(() => Assert.Single(store.Writes));
        Assert.EndsWith("/providers/google/complete", http.Paths.Single());
        var complete = JsonSerializer.Deserialize<CompleteProviderSignInRequest>(http.Requests.Single().Body, ScriptedHttp.Json)!;
        Assert.Equal(("handle.value", new string('v', 43)), (complete.Handle, complete.CodeVerifier));
        Assert.DoesNotContain("handle.value", navigation.Uri);
    }

    [Theory]
    [InlineData("ProviderAccountNotFound", "No account uses this Google account's email.", null)]
    [InlineData("ProviderEmailUnverified", "Google can't confirm this account's email.", null)]
    [InlineData("InvalidGrant", "Google sign-in didn't complete.", "GOOGLE-INVALIDGRANT")]
    public void A_returned_Google_failure_shows_Google_s_message(string code, string message, string? reference)
    {
        using var context = Context(new(true, true), out var http, out _, out var flow);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/Identity/login#google-error=" + code);
        var cut = context.Render<LoginForm>(p => p.Add(x => x.AdmissionFlow, flow));
        Assert.Contains(message, cut.Find("[data-testid=login-failure-message]").TextContent);
        if (reference is null) Assert.Empty(cut.FindAll("[data-testid=login-failure-reference]"));
        else Assert.Contains(reference, cut.Find("[data-testid=login-failure-reference]").TextContent);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void With_only_Microsoft_the_panel_reads_as_before()
    {
        var cut = Panel(new(true, false), []);
        Assert.Contains("Microsoft sign-in", cut.Find("[data-testid=provider-links]").TextContent);
        Assert.Contains("A Microsoft account with this account's email can sign in without the password.", cut.Markup);
        Assert.Contains("No Microsoft account has signed in yet.", cut.Find("[data-testid=provider-links-empty]").TextContent);
        Assert.DoesNotContain("Google", cut.Markup);
    }

    [Fact]
    public void With_both_providers_the_panel_lists_every_link_with_its_provider()
    {
        var now = DateTimeOffset.UtcNow;
        var cut = Panel(new(true, true),
        [
            new(SignInProvider.Microsoft, "person@example.invalid", true, now, now),
            new(SignInProvider.Google, "person@example.invalid", false, now, now)
        ]);
        Assert.Contains("Linked sign-in accounts", cut.Find("[data-testid=provider-links]").TextContent);
        Assert.Equal(["Microsoft", "Google"], cut.FindAll("[data-testid=provider-link-provider]").Select(x => x.TextContent.Trim()));
        Assert.Contains("Work or school account", cut.FindAll("[data-testid=provider-link]")[1].TextContent);
    }

    [Fact]
    public void A_Google_link_shows_after_the_host_turned_Google_off()
    {
        var now = DateTimeOffset.UtcNow;
        var cut = Panel(new(false, false), [new(SignInProvider.Google, "person@example.invalid", true, now, now)]);
        Assert.Contains("Google sign-in", cut.Find("[data-testid=provider-links]").TextContent);
        Assert.Contains("Personal account", cut.Find("[data-testid=provider-link]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=provider-link-provider]"));
    }

    private static IRenderedComponent<ProviderLinksPanel> Panel(SignInProviders providers, IReadOnlyList<ProviderLinkView> links)
    {
        var context = Context(providers, out _, out var store, out var flow, links: links);
        context.Services.AddSingleton(flow);
        store.Writes.Add(AuthenticationFlowTests.Session().Session);
        var cut = context.Render<ProviderLinksPanel>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=provider-links]")));
        return cut;
    }

    private static BunitContext Context(SignInProviders providers, out PathRecordingHttp transport, out RecordingStore store,
        out AuthenticationFlow flow, string authorize = Authorize, IReadOnlyList<ProviderLinkView>? links = null)
    {
        var context = new BunitContext();
        var recorded = new PathRecordingHttp();
        recorded.Inner = new ScriptedHttp(request => Task.FromResult<AuthOutcome>(
            request.RequestUri!.AbsolutePath.EndsWith("/start") ? new ProviderRedirect(authorize)
            : request.RequestUri.AbsolutePath.Contains("/providers/links") ? new ProviderLinksRead(links ?? [])
            : AuthenticationFlowTests.Session())) { Providers = providers };
        transport = recorded;
        store = new();
        var http = recorded.Client();
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

    /// <summary>The scripted transport, also recording each counted request's path.</summary>
    public sealed class PathRecordingHttp : DelegatingHandler
    {
        public ScriptedHttp Inner { set => InnerHandler = value; get => (ScriptedHttp)InnerHandler!; }
        public List<(string? Scheme, string? Credential, string Body)> Requests => Inner.Requests;
        public List<string> Paths { get; } = [];
        public HttpClient Client() => new(this) { BaseAddress = new("https://identity.invalid/") };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath != "/api/identity/v2/providers") Paths.Add(request.RequestUri.AbsolutePath);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
