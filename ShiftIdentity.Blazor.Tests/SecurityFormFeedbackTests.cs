using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// Every security form and popup treats a repeated failure the same way, through one form (<see cref="AuthForm"/>) and
/// one error panel (<see cref="AuthError"/>): each press closes the error on screen first, a press that the form's own
/// checks refuse sends nothing, one attempt runs at a time, and a failure opens its error again. The source check keeps
/// new forms on these two components; the forms' own tests show each of them doing it.
/// </summary>
[Trait("Category", "Ui")]
public sealed class SecurityFormFeedbackTests
{
    private const string FeedbackModule = "./_content/ShiftSoftware.ShiftIdentity.Dashboard.Blazor/auth-feedback.js";

    // The deployed forms, which run only without the authority, report each failure in a message dialog of their own.
    private static readonly Dictionary<string, int> DeployedForms = new()
    {
        ["Pages/UserManager/ChangePasswordForm.razor"] = 1,
        ["Pages/UserManager/MfaForm.razor"] = 1,
        ["Pages/UserManager/TotpEnrollmentForm.razor"] = 1,
        ["Pages/UserManager/ResetPassword.razor"] = 1,
        ["Shared/AuthForm.razor"] = 1
    };

    // The deployed authenticator setup shows a load failure next to its Retry button, which reloads the whole step.
    private static readonly Dictionary<string, int> DeployedErrorAlerts = new() { ["Pages/UserManager/TotpEnrollmentForm.razor"] = 1 };

    [Fact]
    public void Every_security_form_submits_through_the_shared_form_and_binds_its_error_panel()
    {
        var project = Path.Combine(RepositoryRoot(), "ShiftIdentity.Dashboard.Blazor");
        var files = new[] { "Pages/Auth", "Pages/UserManager" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(project, folder), "*.razor", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(project, "Shared"), "*.razor", SearchOption.TopDirectoryOnly));
        var problems = new List<string>();
        foreach (var file in files)
        {
            var name = Path.GetRelativePath(project, file).Replace('\\', '/');
            var source = File.ReadAllText(file);
            var forms = Regex.Count(source, @"<(EditForm|form)\b");
            if (forms != DeployedForms.GetValueOrDefault(name))
                problems.Add($"{name}: {forms} EditForm or form element(s). A security step submits through <AuthForm>, which closes the error on screen before each press.");
            var alerts = Regex.Count(source, @"<MudAlert\b[^>]*Severity\.Error");
            if (alerts != DeployedErrorAlerts.GetValueOrDefault(name))
                problems.Add($"{name}: {alerts} error MudAlert(s). A failed press shows its reason in an <AuthError>, which opens again on a repeated failure.");
            foreach (Match panel in Regex.Matches(source, @"<AuthError\b[^>]*>"))
                if (!panel.Value.Contains("@bind-Message=", StringComparison.Ordinal))
                    problems.Add($"{name}: {panel.Value} binds no message. Use @bind-Message, so that the panel clears its message when it closes.");
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        Assert.Contains(files, x => x.EndsWith("MfaOptOutForm.razor", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_press_closes_the_error_on_screen_before_the_attempt_and_the_same_failure_opens_it_again()
    {
        await using var context = Context();
        var close = context.JSInterop.SetupModule(FeedbackModule).SetupVoid("waitForClose", _ => true);
        var cut = context.Render<Screen>(p => p.Add(x => x.Attempt, _ => Task.FromResult<string?>("Not accepted.")));
        cut.Instance.Entries[0].Code = "123456";
        await cut.Find("form").SubmitAsync();
        Assert.Equal("Not accepted.", cut.Find(".auth-feedback-slot[data-open=true] [data-testid=screen-error]").TextContent);
        Assert.Empty(close.Invocations);

        var retry = cut.Find("form").SubmitAsync();
        cut.WaitForAssertion(() => Assert.Single(close.Invocations));
        // The panel is closing and the attempt waits for it; the button is disabled meanwhile.
        Assert.Empty(cut.FindAll(".auth-feedback-slot[data-open=true]"));
        Assert.Equal(1, cut.Instance.Attempts);
        Assert.True(cut.Find("button[type=submit]").HasAttribute("disabled"));
        close.SetVoidResult();
        await retry;
        Assert.Equal(2, cut.Instance.Attempts);
        cut.WaitForAssertion(() => Assert.Equal("Not accepted.", cut.Find(".auth-feedback-slot[data-open=true] [data-testid=screen-error]").TextContent));
    }

    // The owner's case: a code too short to send. The press used to leave the previous error in place.
    [Theory]
    [InlineData("527")]
    [InlineData("")]
    public async Task A_press_that_the_form_refuses_closes_the_error_on_screen_sends_nothing_and_focuses_the_field(string code)
    {
        await using var context = Context();
        var module = context.JSInterop.SetupModule(FeedbackModule);
        var close = module.SetupVoid("waitForClose", _ => true); close.SetVoidResult();
        var focus = module.SetupVoid("focusInvalid", _ => true); focus.SetVoidResult();
        var cut = context.Render<Screen>(p => p.Add(x => x.Attempt, _ => Task.FromResult<string?>("Not accepted.")));
        cut.Instance.Entries[0].Code = "123456";
        await cut.Find("form").SubmitAsync();
        Assert.Single(cut.FindAll("[data-testid=screen-error]"));

        cut.Instance.Entries[0].Code = code;
        await cut.Find("form").SubmitAsync();
        Assert.Single(close.Invocations);
        Assert.Empty(cut.FindAll("[data-testid=screen-error]"));
        Assert.Null(cut.Instance.Error);
        Assert.Equal(1, cut.Instance.Attempts);
        Assert.Equal<object?>(cut.Find("form").Id, Assert.Single(focus.Invocations).Arguments.Single());
        Assert.False(cut.Find("button[type=submit]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task One_attempt_runs_at_a_time_on_a_screen_whichever_of_its_forms_is_pressed()
    {
        await using var context = Context();
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cut = context.Render<Screen>(p => p.Add(x => x.Forms, 2).Add(x => x.Attempt, _ => pending.Task));
        foreach (var entry in cut.Instance.Entries) entry.Code = "123456";
        var first = cut.FindAll("form")[0].SubmitAsync();
        cut.WaitForAssertion(() => Assert.All(cut.FindAll("button[type=submit]"), button => Assert.True(button.HasAttribute("disabled"))));
        await cut.FindAll("form")[1].SubmitAsync();
        await cut.FindAll("form")[0].SubmitAsync();
        Assert.Equal(1, cut.Instance.Attempts);
        pending.SetResult(null);
        await first;
        cut.WaitForAssertion(() => Assert.All(cut.FindAll("button[type=submit]"), button => Assert.False(button.HasAttribute("disabled"))));
        await cut.FindAll("form")[1].SubmitAsync();
        Assert.Equal(2, cut.Instance.Attempts);
    }

    // The close button of a security dialog is a press too: the error closes first, and the button is disabled while
    // an attempt of the dialog is under way.
    [Fact]
    public async Task Closing_a_security_dialog_closes_its_error_first_and_waits_for_an_attempt()
    {
        await using var context = Context();
        var close = context.JSInterop.SetupModule(FeedbackModule).SetupVoid("waitForClose", _ => true);
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cut = context.Render<Screen>(p => p.Add(x => x.Dialog, true).Add(x => x.Attempt, _ => pending.Task));
        cut.Instance.Entries[0].Code = "123456";
        var attempt = cut.Find("form").SubmitAsync();
        cut.WaitForAssertion(() => Assert.True(cut.Find("[data-testid=security-dialog-close]").HasAttribute("disabled")));
        pending.SetResult("Not accepted.");
        await attempt;
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid=security-dialog-close]").HasAttribute("disabled")));
        Assert.Single(cut.FindAll(".auth-feedback-slot[data-open=true] [data-testid=screen-error]"));

        var closing = cut.Find("[data-testid=security-dialog-close]").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Single(close.Invocations));
        Assert.Equal(0, cut.Instance.Closes);
        close.SetVoidResult();
        await closing;
        Assert.Equal(1, cut.Instance.Closes);
        Assert.Empty(cut.FindAll("[data-testid=screen-error]"));
    }

    [Fact]
    public async Task An_error_panel_needs_a_screen_and_a_bound_message()
    {
        await using var context = Context();
        var outside = Assert.Throws<InvalidOperationException>(() => context.Render<AuthError>(p => p.Add(x => x.Message, "Not accepted.")));
        Assert.Contains("must be inside", outside.Message);
        var unbound = Assert.Throws<InvalidOperationException>(() => context.Render<AuthFeedbackScope>(p => p.AddChildContent<AuthError>(e => e.Add(x => x.Message, "Not accepted."))));
        Assert.Contains("@bind-Message", unbound.Message);
        var form = Assert.Throws<InvalidOperationException>(() => context.Render<AuthForm>(p => p.Add(x => x.Model, new Entry())));
        Assert.Contains("must be inside", form.Message);
    }

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.Services.AddShiftBlazor(o => o.ShiftConfiguration = c => c.BaseAddress = "https://identity.invalid/");
        context.Services.AddShiftIdentityDashboardBlazor(_ => { });
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    // The repository that holds this test; the test runs from its build output inside it.
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "ShiftIdentity.Dashboard.Blazor", "ShiftIdentity.Dashboard.Blazor.csproj")))
                return directory.FullName;
        throw new DirectoryNotFoundException("The ShiftIdentity sources were not found above the test output.");
    }

    public sealed class Entry
    {
        [Required, RegularExpression("^[0-9]{6}$", ErrorMessage = "Enter the code the app shows, using digits only.")]
        public string Code { get; set; } = "";
    }

    /// <summary>
    /// A security screen reduced to its parts: one or more step forms and the screen's error panel, in a security dialog
    /// or on its own. Each attempt returns the error it failed with, or null.
    /// </summary>
    private sealed class Screen : ComponentBase
    {
        [Parameter] public Func<Entry, Task<string?>> Attempt { get; set; } = _ => Task.FromResult<string?>(null);
        [Parameter] public int Forms { get; set; } = 1;
        [Parameter] public bool Dialog { get; set; }
        public List<Entry> Entries { get; } = [];
        public string? Error { get; private set; }
        public int Attempts { get; private set; }
        public int Closes { get; private set; }

        protected override void OnParametersSet()
        {
            while (Entries.Count < Forms) Entries.Add(new());
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (Dialog)
            {
                builder.OpenComponent<LoginBox>(0);
                builder.AddAttribute(1, nameof(LoginBox.Dialog), true);
                builder.AddAttribute(2, nameof(LoginBox.Caption), "Synthetic action");
                builder.AddAttribute(3, nameof(LoginBox.Close), EventCallback.Factory.Create(this, () => { Closes++; }));
                builder.AddAttribute(4, nameof(LoginBox.ChildContent), (RenderFragment)Content);
                builder.CloseComponent();
            }
            else
            {
                builder.OpenComponent<AuthFeedbackScope>(5);
                builder.AddAttribute(6, nameof(AuthFeedbackScope.ChildContent), (RenderFragment)Content);
                builder.CloseComponent();
            }
        }

        private void Content(RenderTreeBuilder builder)
        {
            foreach (var entry in Entries)
            {
                builder.OpenComponent<AuthForm>(0);
                builder.SetKey(entry);
                builder.AddAttribute(1, nameof(AuthForm.Model), entry);
                builder.AddAttribute(2, nameof(AuthForm.Submit), EventCallback.Factory.Create(this, async () => { Attempts++; Error = await Attempt(entry); }));
                builder.AddAttribute(3, nameof(AuthForm.ChildContent), (RenderFragment<bool>)(attempting => button =>
                {
                    button.OpenElement(0, "button");
                    button.AddAttribute(1, "type", "submit");
                    button.AddAttribute(2, "disabled", attempting);
                    button.AddContent(3, "Continue");
                    button.CloseElement();
                }));
                builder.CloseComponent();
            }
            builder.OpenComponent<AuthError>(4);
            builder.AddAttribute(5, nameof(AuthError.Message), Error);
            builder.AddAttribute(6, nameof(AuthError.MessageChanged), EventCallback.Factory.Create<string?>(this, value => Error = value));
            builder.AddAttribute(7, nameof(AuthError.TestId), "screen-error");
            builder.CloseComponent();
        }
    }
}
