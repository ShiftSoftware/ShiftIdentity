using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftIdentity.Blazor;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.UserManager;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>The staged administrator account controls: password set, username, email and active status.</summary>
[Trait("Category", "Ui")]
public sealed class AdminAccountComponentTests
{
    [Theory]
    [InlineData("permission")]
    [InlineData("self")]
    [InlineData("profile")]
    public async Task Account_controls_appear_only_for_an_operator_managing_another_account(string scenario)
    {
        await using var context = Context(out var ui, out var transport);
        transport.CanManageAccount = scenario != "permission";
        await ui.Store.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var cut = context.Render<AdmissionAccountPanel>(p => p.Add(x => x.Context, ui)
            .Add(x => x.Administrator, scenario != "profile").Add(x => x.UserKey, scenario == "self" ? "17" : "42"));
        cut.WaitForAssertion(() => Assert.Contains("Synthetic User", cut.Markup));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Set password");
        Assert.Empty(cut.FindAll("[data-testid=account-status-toggle]"));
        Assert.Empty(transport.Posts);
    }

    [Fact]
    public async Task Password_set_sends_the_next_login_flag_reports_policy_refusal_and_clears_the_field_on_success()
    {
        await using var context = Context(out var ui, out var transport);
        transport.Respond = call => Task.FromResult<AuthOutcome>(call == 1
            ? new AuthenticationRefused(AuthenticationFailure.InvalidNewPassword, PasswordPolicyFailure.TooShort)
            : new AdminAccountChanged(AdminAccountChange.Password, true, 2));
        var previous = AuthenticationFlowTests.Session().Session; await ui.Store.StoreTokenAsync(previous);
        var cut = Render(context, ui);
        cut.Find("[data-testid=account-password] input").Input("short");
        cut.Find("[data-testid=account-password-require-change] input[type=checkbox]").Change(false);
        cut.FindAll("button").Single(b => b.TextContent == "Set password").Click();
        cut.WaitForAssertion(() => Assert.Contains("does not meet the policy", cut.Find("[data-testid=account-change-result]").TextContent));
        var post = Assert.Single(transport.Posts);
        Assert.Equal("/api/identity/v2/admin/password", post.Path);
        Assert.Contains("\"requireChangeAtNextLogin\":false", post.Body); Assert.Contains("\"userID\":42", post.Body);
        Assert.Equal("short", cut.Find("[data-testid=account-password] input").GetAttribute("value"));
        var reads = transport.Reads;

        cut.Find("[data-testid=account-password] input").Input("A much longer synthetic phrase 42");
        cut.FindAll("button").Single(b => b.TextContent == "Set password").Click();
        cut.WaitForAssertion(() => Assert.Contains("Password set.", cut.Find("[data-testid=account-change-result]").TextContent));
        Assert.Equal(2, transport.Posts.Count);
        cut.WaitForAssertion(() => Assert.True(transport.Reads > reads));
        cut.WaitForAssertion(() => Assert.True(string.IsNullOrEmpty(cut.Find("[data-testid=account-password] input").GetAttribute("value"))));
        Assert.Same(previous, Assert.Single(context.Services.GetRequiredService<RecordingStore>().Writes));
        Assert.False(ui.Flow.Busy);
    }

    [Theory]
    [InlineData("sent")]
    [InlineData("unconfirmed")]
    [InlineData("silent")]
    public async Task Email_change_sends_the_verification_choice_and_reports_delivery(string delivery)
    {
        await using var context = Context(out var ui, out var transport);
        transport.Respond = _ => Task.FromResult<AuthOutcome>(new AdminAccountChanged(AdminAccountChange.Email, true, 2, delivery switch
        {
            "sent" => new SecurityDeliveryRequested(),
            "unconfirmed" => new AuthenticationRefused(AuthenticationFailure.Unavailable),
            _ => null
        }));
        await ui.Store.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var cut = Render(context, ui);
        cut.Find("[data-testid=account-email] input").Input("new@example.invalid");
        if (delivery == "silent") cut.Find("[data-testid=account-email-verify] input[type=checkbox]").Change(false);
        cut.FindAll("button").Single(b => b.TextContent == "Change email").Click();
        var expected = delivery switch
        {
            "sent" => "a verification link was sent to it",
            "unconfirmed" => "could not confirm that the verification email was sent",
            _ => "Email changed."
        };
        cut.WaitForAssertion(() => Assert.Contains(expected, cut.Find("[data-testid=account-change-result]").TextContent));
        var post = Assert.Single(transport.Posts);
        Assert.Equal("/api/identity/v2/admin/email", post.Path);
        Assert.Contains("\"email\":\"new@example.invalid\"", post.Body);
        Assert.Contains(delivery == "silent" ? "\"sendVerification\":false" : "\"sendVerification\":true", post.Body);
        if (delivery == "silent") Assert.DoesNotContain("verification link", cut.Find("[data-testid=account-change-result]").TextContent);
    }

    [Fact]
    public async Task Duplicate_identifier_is_reported_distinctly_and_keeps_the_entry()
    {
        await using var context = Context(out var ui, out var transport);
        transport.Respond = _ => Task.FromResult<AuthOutcome>(new AuthenticationRefused(AuthenticationFailure.DuplicateIdentifier));
        await ui.Store.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var cut = Render(context, ui);
        cut.Find("[data-testid=account-username] input").Input("taken");
        cut.FindAll("button").Single(b => b.TextContent == "Change username").Click();
        cut.WaitForAssertion(() => Assert.Contains("Another account already uses this username or email.", cut.Find("[data-testid=account-change-result]").TextContent));
        var post = Assert.Single(transport.Posts);
        Assert.Equal("/api/identity/v2/admin/username", post.Path); Assert.Contains("\"username\":\"taken\"", post.Body);
        Assert.Equal("taken", cut.Find("[data-testid=account-username] input").GetAttribute("value"));
        Assert.Equal(2, transport.Reads);
    }

    [Fact]
    public async Task Status_toggle_reloads_the_account_and_hides_edit_controls_while_inactive()
    {
        await using var context = Context(out var ui, out var transport);
        transport.Respond = _ =>
        {
            transport.Active = !transport.Active;
            return Task.FromResult<AuthOutcome>(new AdminAccountChanged(AdminAccountChange.Active, true, 2));
        };
        await ui.Store.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var cut = Render(context, ui);
        Assert.Contains("Status: Active", cut.Find("[data-testid=account-status]").TextContent);
        cut.Find("[data-testid=account-status-toggle]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Status: Inactive", cut.Find("[data-testid=account-status]").TextContent));
        Assert.Equal("Activate account", cut.Find("[data-testid=account-status-toggle]").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid=account-password]")); Assert.Empty(cut.FindAll("[data-testid=account-username]"));
        Assert.Contains("Account deactivated.", cut.Find("[data-testid=account-change-result]").TextContent);
        Assert.Contains("\"active\":false", Assert.Single(transport.Posts).Body);

        cut.Find("[data-testid=account-status-toggle]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Status: Active", cut.Find("[data-testid=account-status]").TextContent));
        Assert.Single(cut.FindAll("[data-testid=account-password]"));
        Assert.Contains("\"active\":true", transport.Posts.Last().Body);
        Assert.Contains("Account activated.", cut.Find("[data-testid=account-change-result]").TextContent);
    }

    private static IRenderedComponent<AdmissionAccountPanel> Render(BunitContext context, AdmissionUiContext ui)
    {
        var cut = context.Render<AdmissionAccountPanel>(p => p.Add(x => x.Context, ui).Add(x => x.Administrator, true).Add(x => x.UserKey, "42"));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=account-status-toggle]")));
        return cut;
    }

    private static BunitContext Context(out AdmissionUiContext ui, out AccountTransport transport)
    {
        var context = new BunitContext(); var store = new RecordingStore();
        transport = new(); var http = new HttpClient(transport) { BaseAddress = new Uri("https://identity.invalid/") };
        ui = new(new AuthenticationFlow(http, store.Session), store.Session, http);
        context.Services.AddSingleton(store);
        context.Services.AddShiftBlazor(o => o.ShiftConfiguration = c => c.BaseAddress = "https://identity.invalid/");
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    /// <summary>Operator 17 manages target 42. Reads reflect the transport's current active flag.</summary>
    private sealed class AccountTransport : HttpMessageHandler
    {
        public Func<int, Task<AuthOutcome>> Respond { get; set; } = _ => Task.FromResult<AuthOutcome>(new AuthenticationRefused(AuthenticationFailure.Unavailable));
        public bool Active { get; set; } = true;
        public bool CanManageAccount { get; set; } = true;
        public int Reads { get; private set; }
        public List<(string Path, string Body)> Posts { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                Posts.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync(cancellationToken)));
                return new(HttpStatusCode.OK) { Content = JsonContent.Create<AuthOutcome>(await Respond(Posts.Count)) };
            }
            Reads++;
            var id = request.RequestUri!.Segments.Last() == "account" ? 17 : long.Parse(request.RequestUri.Segments.Last());
            var account = new AdmissionAccount(id, id == 17 ? "operator" : "synthetic", "Synthetic User", false, false, false,
                "saved@example.invalid", false, true, id == 17, id == 17, IsActive: id == 17 || Active, CanManageAccount: id == 17 && CanManageAccount);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(account) };
        }
    }
}
