using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using ShiftSoftware.ShiftBlazor.Components;
using ShiftSoftware.ShiftBlazor.Enums;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Blazor;
using ShiftSoftware.ShiftIdentity.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Blazor.Handlers;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.User;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services;
using ShiftSoftware.TypeAuth.Blazor.Extensions;
using ShiftSoftware.TypeAuth.Core;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

[Trait("Category", "Ui"), Collection("User form")]
public sealed class AdministratorConfirmationUiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task User_list_keeps_selected_users_while_the_confirmation_dialog_is_open(bool cancel)
    {
        var storage = new RecordingStore(); await storage.Session.StoreTokenAsync(Session());
        var transport = new ConfirmationTransport();
        await using var context = Context(storage.Session, transport);
        var dialogs = context.Render<MudDialogProvider>();
        var cut = context.Render<UserList>();
        var list = cut.FindComponent<ShiftList<UserListDTO>>().Instance;
        list.SelectState.Total = 2;
        list.SelectState.Toggle(new UserListDTO { ID = "42" });
        list.SelectState.Toggle(new UserListDTO { ID = "43" });
        cut.Render();
        var action = cut.FindComponents<ActionButton<UserListDTO>>().Single(x => x.Instance.Endpoint!.EndsWith("ResetTotp"));
        action.Instance.Confirm = false;
        var clicking = cut.InvokeAsync(() => action.Instance.OnClick.InvokeAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        dialogs.WaitForElement("[data-testid=administrator-confirmation-form] input");
        Assert.Equal(new[] { "42", "43" }, list.SelectState.Items.Select(x => x.ID).Order());
        if (cancel) dialogs.Find("[data-testid=administrator-confirmation-cancel]").Click();
        else
        {
            dialogs.Find("[data-testid=administrator-confirmation-form] input").Input("correct");
            dialogs.Find("[data-testid=administrator-confirmation-form]").Submit();
        }
        await clicking;
        Assert.Equal(cancel ? 0 : 1, transport.Commits);
        Assert.Equal(cancel ? 1 : 2, transport.Mutations.Count);
        Assert.All(transport.Mutations, body => { Assert.Contains("42", body); Assert.Contains("43", body); });
        if (cancel) Assert.Equal(new[] { "42", "43" }, list.SelectState.Items.Select(x => x.ID).Order());
    }

    [Theory]
    [InlineData("password")]
    [InlineData("mfa")]
    [InlineData("cancel")]
    [InlineData("wrong")]
    [InlineData("switch")]
    public async Task Normal_user_form_preserves_edits_during_confirmation_and_only_success_continues(string scenario)
    {
        var storage = new RecordingStore(); var original = Session(); await storage.Session.StoreTokenAsync(original);
        var transport = new ConfirmationTransport { Mfa = scenario == "mfa" };
        await using var context = Context(storage.Session, transport);
        var dialogs = context.Render<MudDialogProvider>();
        var cut = context.Render<UserForm>(p => p.Add(x => x.Key, "42"));
        var form = cut.FindComponent<ShiftEntityForm<UserDTO>>();
        cut.WaitForState(() => form.Instance.TaskInProgress == FormTasks.None);
        await cut.InvokeAsync(form.Instance.EditItem);
        cut.WaitForAssertion(() => Assert.Equal(FormModes.Edit, form.Instance.Mode));
        var draft = form.Instance.Value;
        draft.FullName = "Unsaved operator edit";
        cut.Find("[data-testid=user-email] input").Change("draft@example.invalid");
        var location = context.Services.GetRequiredService<NavigationManager>().Uri;
        cut.Find("form").Submit();
        dialogs.WaitForElement("[data-testid=administrator-confirmation-form] input");
        Assert.Same(draft, form.Instance.Value); Assert.Equal("draft@example.invalid", draft.Email);
        Assert.Equal("Unsaved operator edit", draft.FullName); Assert.Equal(location, context.Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Single(transport.Mutations); Assert.Equal(0, transport.Commits);
        if (scenario == "switch") await storage.Session.StoreTokenAsync(Session("different-actor"));
        if (scenario != "cancel")
        {
            dialogs.Find("[data-testid=administrator-confirmation-form] input").Input(scenario == "wrong" ? "wrong" : "correct");
            dialogs.Find("[data-testid=administrator-confirmation-form]").Submit();
        }
        if (scenario == "mfa")
        {
            dialogs.WaitForAssertion(() => Assert.Contains("Authenticator code", dialogs.Markup));
            Assert.Single(transport.Mutations); Assert.Single(storage.Writes);
            dialogs.Find("[data-testid=administrator-confirmation-form] input").Input("123456");
            dialogs.Find("[data-testid=administrator-confirmation-form]").Submit();
        }
        if (scenario is "cancel" or "wrong" or "switch")
        {
            if (scenario != "cancel") dialogs.WaitForElement("[data-testid=administrator-confirmation-error]");
            dialogs.Find("[data-testid=administrator-confirmation-cancel]").Click();
            cut.WaitForState(() => form.Instance.TaskInProgress == FormTasks.None);
            Assert.Single(transport.Mutations); Assert.Equal(0, transport.Commits);
            Assert.Equal("draft@example.invalid", form.Instance.Value.Email);
            Assert.Equal("Unsaved operator edit", form.Instance.Value.FullName);
            Assert.Equal(scenario == "switch" ? 2 : 1, storage.Writes.Count);
        }
        else
        {
            cut.WaitForAssertion(() => Assert.Equal(1, transport.Commits));
            Assert.Equal(2, transport.Mutations.Count);
            Assert.Equal(transport.Mutations[0], transport.Mutations[1]);
            Assert.Equal(2, storage.Writes.Count);
        }
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("uncertain")]
    [InlineData("cancel")]
    [InlineData("success")]
    public async Task Bulk_selection_is_frozen_and_only_explicit_confirmation_refusal_can_resume_once(string scenario)
    {
        var storage = new RecordingStore(); await storage.Session.StoreTokenAsync(Session());
        var prompt = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmation = new ConfirmationStub(() => prompt.Task);
        var continuation = new AdministratorActionContinuation(storage.Session, confirmation, new("https://identity.invalid/"));
        var sent = new List<string>(); var commits = 0;
        async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken ct)
        {
            sent.Add(await request.Content!.ReadAsStringAsync(ct));
            if (sent.Count == 1) return scenario == "permission" ? new(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new { Message = new { Title = "Permission denied", Body = "Detailed server reason" } }) }
                : scenario == "uncertain" ? new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("Outcome unknown") } : Required();
            commits++; return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { }) };
        }
        var selection = new SelectStateDTO<UserListDTO> { Items = [new() { ID = "42" }, new() { ID = "43" }] };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://identity.invalid/api/IdentityUser/ResetTotp") { Content = JsonContent.Create(selection) };
        request.Headers.Authorization = new("Bearer", storage.Session.GetToken());
        var pending = continuation.SendAsync(request, Send, CancellationToken.None);
        if (scenario is "success" or "cancel")
        {
            Assert.Equal(1, confirmation.Calls);
            using var duplicate = new HttpRequestMessage(HttpMethod.Post, request.RequestUri) { Content = JsonContent.Create(selection) };
            duplicate.Headers.Authorization = request.Headers.Authorization;
            using var refused = await continuation.SendAsync(duplicate, Send, CancellationToken.None);
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal(2, selection.Items.Count());
            if (scenario == "success") { var confirmed = Session(); await storage.Session.StoreTokenAsync(confirmed); prompt.SetResult(confirmed.Token); }
            else prompt.SetResult(null);
        }
        using var result = await pending;
        Assert.Equal(scenario == "success" ? 1 : 0, commits);
        Assert.Equal(scenario == "success" ? 2 : 1, sent.Count);
        Assert.All(sent, body => { Assert.Contains("42", body); Assert.Contains("43", body); });
        if (scenario == "success") Assert.Equal(sent[0], sent[1]);
        if (scenario == "permission") Assert.Contains("Detailed server reason", await result.Content.ReadAsStringAsync());
        Assert.Equal(scenario is "success" or "cancel" ? 1 : 0, confirmation.Calls);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("account")]
    [InlineData("same-account-new-session")]
    [InlineData("wrong-result-actor")]
    public async Task Late_confirmation_never_overwrites_a_changed_or_cancelled_session(string scenario)
    {
        var storage = new RecordingStore(); var current = Session(); await storage.Session.StoreTokenAsync(current);
        var delayed = new TaskCompletionSource<AuthOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var flow = new AuthenticationFlow(new ScriptedHttp(r => r.RequestUri!.AbsolutePath.EndsWith("admin-confirmation")
            ? Task.FromResult<AuthOutcome>(Challenge(AuthenticationStep.Password)) : r.RequestUri.AbsolutePath.EndsWith("cancel")
                ? Task.FromResult<AuthOutcome>(new OperationCancelled()) : delayed.Task).Client(), storage.Session);
        await flow.BeginAdministratorConfirmationAsync(current.Token);
        var submitted = flow.ConfirmAdministratorPasswordAsync("correct");
        if (scenario == "cancel") await flow.CancelAsync();
        if (scenario is "account" or "same-account-new-session") await storage.Session.StoreTokenAsync(Session(scenario == "account" ? "other" : "operator"));
        var before = storage.Session.GetToken();
        delayed.SetResult(new SessionIssued(Session(scenario == "wrong-result-actor" ? "other" : "operator")));
        Assert.IsType<AuthenticationRefused>(await submitted);
        Assert.Equal(before, storage.Session.GetToken());
    }

    private static BunitContext Context(IdentitySession session, ConfirmationTransport transport)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(session);
        context.Services.AddShiftBlazor(o => o.ShiftConfiguration = c => c.BaseAddress = "https://identity.invalid/api/");
        context.Services.AddShiftIdentity("synthetic", "https://identity.invalid/api/", "https://identity.invalid/");
        context.Services.AddSingleton(new StagedAuthorityHttpClient(transport) { BaseAddress = new("https://identity.invalid/") });
        context.Services.AddShiftIdentityDashboardBlazor(o => o.StagedAuthority = true);
        context.Services.AddScoped(sp => new HttpClient(new TokenMessageHandlerWithAutoRefresh(session, sp.GetRequiredService<MessageService>(),
            sp.GetRequiredService<AdministratorActionContinuation>()) { InnerHandler = transport }) { BaseAddress = new("https://identity.invalid/api/") });
        var auth = context.AddAuthorization(); auth.SetAuthorized("synthetic");
        auth.SetClaims(new Claim(TypeAuthClaimTypes.AccessTree, "{\"ShiftIdentityActions\":{\"Users\":[\"r\",\"w\",\"d\"]}}"));
        context.Services.AddTypeAuth(o => o.AddActionTree<ShiftIdentityActions>());
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    internal static TokenDTO Session(string subject = "operator")
    {
        string Encode(object value) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new TokenDTO
        {
            Token = Encode(new { alg = "none" }) + "." + Encode(new { sub = subject, jti = Guid.NewGuid(), shift_purpose = "access", shift_schema = "2", exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds() }) + ".",
            RefreshToken = Guid.NewGuid().ToString(), TokenLifeTimeInSeconds = 600,
            UserData = new() { ID = subject, Username = "synthetic", FullName = "Synthetic Operator" }
        };
    }
    private static ChallengeRequired Challenge(AuthenticationStep step) => new(new(step, "confirmation-handle", DateTimeOffset.UtcNow.AddMinutes(5), AuthenticationOperationPurpose.AdministratorConfirmation));
    private static HttpResponseMessage Required() => new(HttpStatusCode.Forbidden)
    { Content = JsonContent.Create(new { Message = new { Title = "Confirm", Body = "Original server detail", For = AdministratorAuthentication.RequiredMessage } }) };

    private sealed class ConfirmationStub(Func<Task<string?>> confirm) : IAdministratorConfirmation
    {
        public int Calls { get; private set; }
        public Task<string?> ConfirmAsync(string currentAccess, CancellationToken cancellationToken) { Calls++; return confirm(); }
    }

    private sealed class ConfirmationTransport : HttpMessageHandler
    {
        public bool Mfa { get; init; }
        public List<string> Mutations { get; } = [];
        public int Commits { get; private set; }
        private readonly TokenDTO confirmed = Session();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/api/identity/v2/"))
            {
                var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
                AuthOutcome outcome = path.EndsWith("/admin-confirmation") ? Challenge(AuthenticationStep.Password)
                    : path.EndsWith("/cancel") ? new OperationCancelled()
                    : body.Contains("wrong") ? new AuthenticationRefused(AuthenticationFailure.InvalidProof)
                    : path.EndsWith("/password") && Mfa ? Challenge(AuthenticationStep.ExistingMfa) : new SessionIssued(confirmed);
                return new(HttpStatusCode.OK) { Content = JsonContent.Create<AuthOutcome>(outcome) };
            }
            if (path == "/api/IdentityUser/42")
            {
                var user = new UserDTO { ID = "42", Username = "synthetic-target", FullName = "Saved user", IsActive = true, Email = "saved@example.invalid",
                    AccessTree = "{}", CompanyBranchID = new ShiftEntitySelectDTO { Value = "1", Text = "Synthetic Branch" } };
                if (request.Method == HttpMethod.Put)
                {
                    Mutations.Add(await request.Content!.ReadAsStringAsync(ct));
                    if (request.Headers.Authorization?.Parameter != confirmed.Token) return Required();
                    Commits++;
                    user = JsonSerializer.Deserialize<UserDTO>(Mutations.Last(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                }
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new ShiftEntityResponse<UserDTO>(user)) };
            }
            if (path == "/api/IdentityUser/ResetTotp")
            {
                Mutations.Add(await request.Content!.ReadAsStringAsync(ct));
                if (request.Headers.Authorization?.Parameter != confirmed.Token) return Required();
                Commits++;
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new ShiftEntityResponse<IList<UserListDTO>>([new() { ID = "42" }, new() { ID = "43" }])) };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"value\":[]}", System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
