using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftIdentity.Blazor;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.UserManager;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

[Trait("Category", "Ui")]
public sealed class SecurityEmailDeliveryComponentTests
{
    [Theory]
    [InlineData("profile-verification")]
    [InlineData("admin-verification")]
    [InlineData("admin-reset")]
    [InlineData("manual-reset")]
    public async Task Authenticated_requests_wait_for_handoff_and_allow_explicit_retry_without_changing_session(string action)
    {
        var pending = new TaskCompletionSource<AuthOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var context = Context(out var ui, out var handler, requestNumber => requestNumber == 1 ? pending.Task
            : Task.FromResult<AuthOutcome>(action == "manual-reset"
                ? new ManualPasswordResetIssued("opaque", "synthetic", DateTimeOffset.UtcNow.AddMinutes(10))
                : new SecurityDeliveryRequested()));
        var previous = AuthenticationFlowTests.Session().Session; await ui.Store.StoreTokenAsync(previous);
        var cut = context.Render<AdmissionAccountPanel>(p => p.Add(x => x.Context, ui)
            .Add(x => x.Administrator, action != "profile-verification").Add(x => x.UserKey, "42"));
        var label = action switch { "admin-reset" => "Send password reset email", "manual-reset" => "Create manual password reset link", _ => "Send email verification link" };
        var sending = cut.FindAll("button").Single(b => b.TextContent == label).ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.True(cut.FindAll("button").Single(b => b.TextContent == label).HasAttribute("disabled")));
        Assert.True(ui.Flow.Busy); Assert.Empty(cut.FindAll("[data-testid=account-delivery-result]"));
        Assert.Same(previous, Assert.Single(context.Services.GetRequiredService<RecordingStore>().Writes));

        await cut.InvokeAsync(() => pending.SetResult(new AuthenticationRefused(AuthenticationFailure.Unavailable)));
        await sending;
        cut.WaitForAssertion(() => Assert.Contains(action == "manual-reset" ? "Please wait and try again." : "Check the saved email inbox, then wait before trying again.",
            cut.Find("[data-testid=account-delivery-result]").TextContent));
        Assert.False(ui.Flow.Busy); Assert.Single(handler.Posts);
        Assert.DoesNotContain("check your permission", cut.Find("[data-testid=account-delivery-result]").TextContent);

        cut.FindAll("button").Single(b => b.TextContent == label).Click();
        cut.WaitForAssertion(() => Assert.Equal(2, handler.Posts.Count));
        if (action == "manual-reset") Assert.Single(cut.FindAll("[data-testid=manual-reset-link]"));
        else Assert.Contains("If no link arrives, wait before trying again.", cut.Find("[data-testid=account-delivery-result]").TextContent);
        Assert.Same(previous, Assert.Single(context.Services.GetRequiredService<RecordingStore>().Writes));
        var expectedRoute = action switch
        {
            "profile-verification" => "/api/identity/v2/email-verification/request-current",
            "admin-verification" => "/api/identity/v2/email-verification/admin",
            _ => "/api/identity/v2/password-reset/admin"
        };
        Assert.All(handler.Posts, route => Assert.Equal(expectedRoute, route));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Public_request_waits_and_global_failure_can_be_retried_with_generic_account_wording(bool verification)
    {
        var pending = new TaskCompletionSource<AuthOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var context = Context(out var ui, out var handler, number => number == 1 ? pending.Task : Task.FromResult<AuthOutcome>(new SecurityDeliveryRequested()));
        var previous = AuthenticationFlowTests.Session().Session; await ui.Store.StoreTokenAsync(previous);
        var cut = context.Render<SecurityEmailRequestForm>(p => p.Add(x => x.Context, ui).Add(x => x.Verification, verification));
        cut.Find("input").Input("saved@example.invalid");
        var sending = cut.Find("form").SubmitAsync();
        cut.WaitForAssertion(() => Assert.True(cut.Find("button[type=submit]").HasAttribute("disabled")));
        Assert.Contains("Sending request…", cut.Markup); Assert.Empty(cut.FindAll("[data-testid=delivery-requested]"));
        await cut.InvokeAsync(() => pending.SetResult(new AuthenticationRefused(AuthenticationFailure.Unavailable)));
        await sending;
        cut.WaitForAssertion(() => Assert.Contains("Email requests are unavailable right now", cut.Find("[data-testid=delivery-error]").TextContent));
        Assert.Single(handler.Posts); Assert.False(ui.Flow.Busy);
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("If an eligible account matches, check its saved email inbox. If no link arrives, wait before trying again.",
            cut.Find("[data-testid=delivery-requested]").TextContent));
        Assert.Empty(cut.FindAll("[data-testid=delivery-error]")); Assert.Equal(2, handler.Posts.Count);
        Assert.Same(previous, Assert.Single(context.Services.GetRequiredService<RecordingStore>().Writes));
    }

    [Fact]
    public async Task Authenticated_permission_refusal_remains_distinct_from_delivery_failure()
    {
        using var context = Context(out var ui, out var handler, _ => Task.FromResult<AuthOutcome>(new AuthenticationRefused(AuthenticationFailure.ClientDenied)));
        await ui.Store.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var cut = context.Render<AdmissionAccountPanel>(p => p.Add(x => x.Context, ui).Add(x => x.Administrator, true).Add(x => x.UserKey, "42"));
        cut.FindAll("button").Single(b => b.TextContent == "Send password reset email").Click();
        cut.WaitForAssertion(() => Assert.Contains("check your permission", cut.Find("[data-testid=account-delivery-result]").TextContent));
        Assert.Single(handler.Posts); Assert.Single(context.Services.GetRequiredService<RecordingStore>().Writes);
    }

    private static BunitContext Context(out AdmissionUiContext ui, out DeliveryResponses handler, Func<int, Task<AuthOutcome>> respond)
    {
        var context = new BunitContext(); var store = new RecordingStore();
        handler = new(respond); var http = new HttpClient(handler) { BaseAddress = new Uri("https://identity.invalid/") };
        ui = new(new AuthenticationFlow(http, store.Session), store.Session, http);
        context.Services.AddSingleton(store);
        context.Services.AddShiftBlazor(o => o.ShiftConfiguration = c => c.BaseAddress = "https://identity.invalid/");
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private sealed class DeliveryResponses(Func<int, Task<AuthOutcome>> respond) : HttpMessageHandler
    {
        public List<string> Posts { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                Posts.Add(request.RequestUri!.AbsolutePath);
                return new(HttpStatusCode.OK) { Content = JsonContent.Create<AuthOutcome>(await respond(Posts.Count)) };
            }
            var id = request.RequestUri!.Segments.Last() == "account" ? 17 : long.Parse(request.RequestUri.Segments.Last());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new AdmissionAccount(id, "synthetic", "Synthetic User", false, false, false,
                "saved@example.invalid", false, true, id == 17, id == 17)) };
        }
    }
}
