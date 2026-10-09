using System.Globalization;
using Bunit;
using Bunit.TestDoubles;
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
/// The phone's side of device sign-in: the page a device's QR code opens. It needs no sign-in. It names the device and
/// the code first, then asks for the username and password of the account the device should use, with a warning that
/// the device gets that account's full access. It never sends the phone's session and never changes it.
/// </summary>
[Trait("Category", "Ui")]
public sealed class DeviceSignInComponentTests
{
    private const string Code = "WDJBMJHT";
    // MudBlazor puts the test ID on the input element itself.
    private const string CodeInput = "input[data-testid=device-code-input]";
    private const string UsernameInput = "input[data-testid=device-username]";
    private const string PasswordInput = "input[data-testid=device-password]";

    private static DeviceAuthorizationView View(DeviceAuthorizationState state, string? account = null) =>
        new("WDJB-MJHT", "Service Screen", account, state, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10));

    private static BunitContext Context(RecordingStore store, ScriptedHttp transport, string address, bool signedIn = false)
    {
        var context = new BunitContext(); var http = transport.Client();
        if (signedIn) store.Writes.Add(AuthenticationFlowTests.Session().Session);
        context.Services.AddSingleton(http); context.Services.AddSingleton(store); context.Services.AddSingleton(store.Session);
        context.Services.AddShiftBlazor(options => options.ShiftConfiguration = config => config.BaseAddress = "https://identity.invalid");
        context.Services.AddShiftIdentityDashboardBlazor(x => x.StagedAuthority = true);
        // The dashboard registration adds its own flow on the identity API root; the test's flow uses the scripted transport.
        context.Services.AddScoped(_ => new AuthenticationFlow(http, store.Session));
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        context.AddAuthorization(); context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(address);
        return context;
    }

    private static BunitNavigationManager Navigation(BunitContext context) => (BunitNavigationManager)context.Services.GetRequiredService<NavigationManager>();

    // The code check, then the account step.
    private static async Task<IRenderedComponent<DeviceSignIn>> AccountStepAsync(BunitContext context)
    {
        var cut = context.Render<DeviceSignIn>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=device-confirm]")));
        await cut.Find("[data-testid=device-confirm]").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(UsernameInput)));
        return cut;
    }

    private static Task SignInAsync(IRenderedComponent<DeviceSignIn> cut, string username = "screen-account", string password = "1")
    {
        cut.Find(UsernameInput).Input(username);
        cut.Find(PasswordInput).Input(password);
        return FormSubmission.SubmitAsync(cut);
    }

    [Fact]
    public void A_person_who_is_not_signed_in_sees_the_device_and_the_code_first()
    {
        var store = new RecordingStore();
        var transport = new ScriptedHttp(_ => Task.FromResult<AuthOutcome>(View(DeviceAuthorizationState.Pending)));
        // As a person may type it: lowercase, no dash.
        using var context = Context(store, transport, "Identity/device?code=wdjbmjht");
        var cut = context.Render<DeviceSignIn>();
        cut.WaitForAssertion(() => Assert.Equal("WDJB-MJHT", cut.Find("[data-testid=device-code]").TextContent.Trim()));
        Assert.Equal("Service Screen is asking to sign in.", cut.Find("[data-testid=device-question]").TextContent.Trim());
        Assert.Equal("Service Screen", cut.Find("[data-testid=device-question] strong").TextContent);
        Assert.Contains("Never use a code someone sent you", cut.Find("[data-testid=device-caution]").TextContent);
        // No login: the page stays here, and the lookup carried no session.
        Assert.EndsWith("Identity/device?code=wdjbmjht", Navigation(context).Uri);
        var lookup = Assert.Single(transport.Requests);
        Assert.Null(lookup.Scheme);
        Assert.Empty(cut.FindAll(UsernameInput));
    }

    [Fact]
    public async Task The_account_step_warns_of_full_access_and_signs_the_device_in_without_the_phone_session()
    {
        var store = new RecordingStore();
        var paths = new List<string>();
        var transport = new ScriptedHttp(request =>
        {
            paths.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            return Task.FromResult<AuthOutcome>(request.Method == HttpMethod.Get
                ? View(DeviceAuthorizationState.Pending) : View(DeviceAuthorizationState.Approved, "Workshop Board"));
        });
        // The phone happens to be signed in. The page neither uses nor changes that session.
        using var context = Context(store, transport, "Identity/device?code=WDJB-MJHT", signedIn: true);
        var session = store.Writes.Single();
        var cut = await AccountStepAsync(context);
        Assert.Contains("Which account should Service Screen use?", cut.Markup);
        Assert.Contains("full access", cut.Find("[data-testid=device-access-warning]").TextContent);
        // The phone's saved sign-in must not fill the fields.
        Assert.Equal("off", cut.Find(UsernameInput).GetAttribute("autocomplete"));
        Assert.Equal("off", cut.Find(PasswordInput).GetAttribute("autocomplete"));
        await SignInAsync(cut, "screen-account", "typed password");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=device-approved]")));
        Assert.Equal("Service Screen is signed in as Workshop Board.", cut.Find("[data-testid=device-approved-as]").TextContent.Trim());
        Assert.Equal(["Service Screen", "Workshop Board"], cut.FindAll("[data-testid=device-approved-as] strong").Select(x => x.TextContent));
        Assert.Contains("Nothing changed on your phone.", cut.Markup);
        Assert.Equal(["GET /api/identity/v2/device/" + Code, "POST /api/identity/v2/device/approve"], paths);
        Assert.All(transport.Requests, x => Assert.Null(x.Scheme));
        var body = transport.Requests.Last().Body;
        Assert.Contains("\"userCode\":\"" + Code + "\"", body);
        Assert.Contains("\"username\":\"screen-account\"", body);
        Assert.Contains("\"password\":\"typed password\"", body);
        Assert.Same(session, store.Writes.Single());
    }

    [Fact]
    public async Task This_isnt_it_refuses_the_code()
    {
        var store = new RecordingStore();
        var transport = new ScriptedHttp(request => Task.FromResult<AuthOutcome>(request.Method == HttpMethod.Get
            ? View(DeviceAuthorizationState.Pending) : View(DeviceAuthorizationState.Denied)));
        using var context = Context(store, transport, "Identity/device?code=WDJB-MJHT");
        var cut = context.Render<DeviceSignIn>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=device-deny]")));
        await cut.Find("[data-testid=device-deny]").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=device-denied]")));
        Assert.Equal(2, transport.Requests.Count);
        Assert.Null(transport.Requests.Last().Scheme);
        Assert.Contains("\"userCode\":\"" + Code + "\"", transport.Requests.Last().Body);
    }

    [Fact]
    public async Task Back_returns_to_the_code_check_and_forgets_the_password()
    {
        var store = new RecordingStore();
        var transport = new ScriptedHttp(_ => Task.FromResult<AuthOutcome>(View(DeviceAuthorizationState.Pending)));
        using var context = Context(store, transport, "Identity/device?code=WDJB-MJHT");
        var cut = await AccountStepAsync(context);
        cut.Find(UsernameInput).Input("screen-account");
        cut.Find(PasswordInput).Input("typed password");
        await cut.Find("[data-testid=device-back]").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=device-code]")));
        await cut.Find("[data-testid=device-confirm]").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(PasswordInput)));
        Assert.Equal("screen-account", cut.Find(UsernameInput).GetAttribute("value"));
        Assert.True(string.IsNullOrEmpty(cut.Find(PasswordInput).GetAttribute("value")));
        Assert.Single(transport.Requests);
    }

    [Theory]
    [InlineData(AuthenticationFailure.InvalidProof, "Username or password is incorrect.")]
    [InlineData(AuthenticationFailure.DeviceSignInNotAllowed, "can't be used to sign in devices")]
    [InlineData(AuthenticationFailure.AccountUnavailable, "This account can't sign in.")]
    [InlineData(AuthenticationFailure.AttemptsExhausted, "Too many attempts")]
    [InlineData(AuthenticationFailure.Unavailable, "couldn't reach the sign-in service")]
    public async Task A_refused_account_stays_on_the_account_step_with_a_message_and_no_password(AuthenticationFailure failure, string text)
    {
        var store = new RecordingStore();
        var transport = new ScriptedHttp(request => Task.FromResult<AuthOutcome>(request.Method == HttpMethod.Get
            ? View(DeviceAuthorizationState.Pending) : new AuthenticationRefused(failure)));
        using var context = Context(store, transport, "Identity/device?code=WDJB-MJHT");
        var cut = await AccountStepAsync(context);
        await SignInAsync(cut);
        cut.WaitForAssertion(() => Assert.Contains(text, cut.Find("[data-testid=device-error]").TextContent));
        Assert.Equal("account", cut.Find("[data-testid=device-sign-in]").GetAttribute("data-view"));
        Assert.Equal("screen-account", cut.Find(UsernameInput).GetAttribute("value"));
        Assert.True(string.IsNullOrEmpty(cut.Find(PasswordInput).GetAttribute("value")));
        // The emptied field does not complain about itself; only the refusal is shown.
        Assert.DoesNotContain("Enter the password.", cut.Markup);
    }

    [Fact]
    public async Task An_empty_password_is_refused_on_the_phone_without_a_request()
    {
        var store = new RecordingStore();
        var transport = new ScriptedHttp(_ => Task.FromResult<AuthOutcome>(View(DeviceAuthorizationState.Pending)));
        using var context = Context(store, transport, "Identity/device?code=WDJB-MJHT");
        var cut = await AccountStepAsync(context);
        await SignInAsync(cut, password: "");
        cut.WaitForAssertion(() => Assert.Contains("Enter the password.", cut.Find("[data-testid=device-error]").TextContent));
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task An_account_that_owes_a_step_is_told_to_sign_in_normally_first()
    {
        var store = new RecordingStore();
        var transport = new ScriptedHttp(request => Task.FromResult<AuthOutcome>(request.Method == HttpMethod.Get
            ? View(DeviceAuthorizationState.Pending)
            : new ChallengeRequired(new(AuthenticationStep.PasswordChange, null, DateTimeOffset.UtcNow.AddMinutes(5)))));
        using var context = Context(store, transport, "Identity/device?code=WDJB-MJHT");
        var cut = await AccountStepAsync(context);
        await SignInAsync(cut);
        cut.WaitForAssertion(() => Assert.Contains("Sign in with it normally once", cut.Find("[data-testid=device-error]").TextContent));
        // Nothing leaves this page: the phone does not sign in.
        Assert.EndsWith("Identity/device?code=WDJB-MJHT", Navigation(context).Uri);
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task Without_a_code_the_page_asks_for_one_and_reads_it_from_the_address()
    {
        var store = new RecordingStore();
        var transport = new ScriptedHttp(_ => Task.FromResult<AuthOutcome>(View(DeviceAuthorizationState.Pending)));
        using var context = Context(store, transport, "Identity/device");
        var cut = context.Render<DeviceSignIn>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=device-code-input]")));
        Assert.Empty(transport.Requests);
        // An incomplete code is refused here, without a request that would count against this address.
        cut.Find(CodeInput).Input("WDJB-MJH");
        await FormSubmission.SubmitAsync(cut);
        cut.WaitForAssertion(() => Assert.Contains("not complete", cut.Find("[data-testid=device-error]").TextContent));
        Assert.Empty(transport.Requests);
        cut.Find(CodeInput).Input("wdjb mjht");
        await FormSubmission.SubmitAsync(cut);
        Assert.EndsWith("Identity/device?code=WDJB-MJHT", Navigation(context).Uri);
        cut.WaitForAssertion(() => Assert.Equal("WDJB-MJHT", cut.Find("[data-testid=device-code]").TextContent.Trim()));
        Assert.Single(transport.Requests);
    }

    [Fact]
    public void A_code_that_matches_nothing_returns_to_the_entry_with_the_code_and_a_message()
    {
        var store = new RecordingStore();
        var transport = new ScriptedHttp(_ => Task.FromResult<AuthOutcome>(new AuthenticationRefused(AuthenticationFailure.InvalidGrant)));
        using var context = Context(store, transport, "Identity/device?code=WDJB-MJHT");
        var cut = context.Render<DeviceSignIn>();
        cut.WaitForAssertion(() => Assert.Contains("couldn't find that code", cut.Find("[data-testid=device-error]").TextContent));
        Assert.Equal("WDJB-MJHT", cut.Find(CodeInput).GetAttribute("value"));
    }

    [Theory]
    [InlineData(DeviceAuthorizationState.Expired, "expired")]
    [InlineData(DeviceAuthorizationState.Consumed, "expired")]
    [InlineData(DeviceAuthorizationState.Approved, "already used")]
    [InlineData(DeviceAuthorizationState.Denied, "refused")]
    public void A_code_that_is_no_longer_pending_cannot_be_used(DeviceAuthorizationState state, string reason)
    {
        var store = new RecordingStore();
        var transport = new ScriptedHttp(_ => Task.FromResult<AuthOutcome>(View(state)));
        using var context = Context(store, transport, "Identity/device?code=WDJB-MJHT");
        var cut = context.Render<DeviceSignIn>();
        cut.WaitForAssertion(() => Assert.Contains(reason, cut.Find("[data-testid=device-ended]").TextContent));
        Assert.Empty(cut.FindAll("[data-testid=device-confirm]"));
        cut.Find("[data-testid=device-another]").Click();
        Assert.EndsWith("Identity/device", Navigation(context).Uri);
    }

    [Fact]
    public async Task An_approval_that_lost_a_race_shows_the_code_as_it_now_is()
    {
        var store = new RecordingStore(); var reads = 0;
        var transport = new ScriptedHttp(request => Task.FromResult<AuthOutcome>(request.Method == HttpMethod.Get
            ? View(++reads == 1 ? DeviceAuthorizationState.Pending : DeviceAuthorizationState.Denied)
            : new AuthenticationRefused(AuthenticationFailure.StaleOperation)));
        using var context = Context(store, transport, "Identity/device?code=WDJB-MJHT");
        var cut = await AccountStepAsync(context);
        await SignInAsync(cut);
        cut.WaitForAssertion(() => Assert.Contains("refused", cut.Find("[data-testid=device-ended]").TextContent));
    }

    [Fact]
    public void An_unreachable_service_offers_to_try_again()
    {
        var store = new RecordingStore();
        var transport = new ScriptedHttp(_ => Task.FromResult<AuthOutcome>(new AuthenticationRefused(AuthenticationFailure.Unavailable)));
        using var context = Context(store, transport, "Identity/device?code=WDJB-MJHT");
        var cut = context.Render<DeviceSignIn>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=device-retry]")));
    }

    // Each sentence is translated whole, so the device and account names sit where each language puts them, in bold.
    [Theory]
    [InlineData("ar-IQ", "يطلب Service Screen تسجيل الدخول.", "تم تسجيل دخول Service Screen بحساب Workshop Board.")]
    [InlineData("ku", "Service Screen داوای چوونەژوورەوە دەکات.", "Service Screen بە هەژماری Workshop Board چووەتە ژوورەوە.")]
    [InlineData("ru", "Service Screen запрашивает вход.", "На Service Screen выполнен вход в аккаунт Workshop Board.")]
    public async Task The_page_reads_in_the_current_culture_with_the_names_in_bold(string culture, string question, string approved)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var store = new RecordingStore();
            var transport = new ScriptedHttp(request => Task.FromResult<AuthOutcome>(request.Method == HttpMethod.Get
                ? View(DeviceAuthorizationState.Pending) : View(DeviceAuthorizationState.Approved, "Workshop Board")));
            using var context = Context(store, transport, "Identity/device?code=WDJB-MJHT");
            var cut = context.Render<DeviceSignIn>();
            cut.WaitForAssertion(() => Assert.Equal(question, cut.Find("[data-testid=device-question]").TextContent.Trim()));
            Assert.Equal("Service Screen", cut.Find("[data-testid=device-question] strong").TextContent);
            Assert.DoesNotContain("Never use", cut.Find("[data-testid=device-caution]").TextContent);
            await cut.Find("[data-testid=device-confirm]").ClickAsync(new());
            cut.WaitForAssertion(() => Assert.Single(cut.FindAll(UsernameInput)));
            Assert.DoesNotContain("full access", cut.Find("[data-testid=device-access-warning]").TextContent);
            await SignInAsync(cut);
            cut.WaitForAssertion(() => Assert.Equal(approved, cut.Find("[data-testid=device-approved-as]").TextContent.Trim()));
            Assert.Equal(["Service Screen", "Workshop Board"], cut.FindAll("[data-testid=device-approved-as] strong").Select(x => x.TextContent));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
