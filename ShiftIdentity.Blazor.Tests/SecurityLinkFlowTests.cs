using System.Net.Http.Json;
using System.Text.Json;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

[Trait("Category", "Ui")]
public sealed class SecurityLinkFlowTests
{
    [Theory]
    [InlineData("request-reset")]
    [InlineData("request-verify")]
    [InlineData("current")]
    [InlineData("admin-reset")]
    [InlineData("manual")]
    [InlineData("admin-verify")]
    [InlineData("open")]
    [InlineData("complete-reset")]
    [InlineData("complete-verify")]
    public async Task No_security_link_action_accepts_a_session_response_or_changes_the_current_session(string action)
    {
        var store = new RecordingStore(); var previous = AuthenticationFlowTests.Session().Session;
        await store.StoreTokenAsync(previous);
        var transport = new ScriptedHttp(_ => Task.FromResult<AuthOutcome>(AuthenticationFlowTests.Session()));
        var flow = new AuthenticationFlow(transport.Client(), store);
        Assert.IsType<AuthenticationRefused>(await Invoke(flow, action));
        Assert.Same(previous, Assert.Single(store.Writes));
        Assert.Null(flow.Pending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completion_uses_only_page_handle_and_never_touches_an_unrelated_session(bool verification)
    {
        var store = new RecordingStore(); var previous = AuthenticationFlowTests.Session().Session;
        await store.StoreTokenAsync(previous);
        var transport = new ScriptedHttp(_ => Task.FromResult<AuthOutcome>(verification ? new EmailVerificationCompleted() : new ReturnToLogin()));
        var flow = new AuthenticationFlow(transport.Client(), store);
        var result = await Invoke(flow, verification ? "complete-verify" : "complete-reset");
        Assert.Equal(verification ? typeof(EmailVerificationCompleted) : typeof(ReturnToLogin), result.GetType());
        var request = Assert.Single(transport.Requests);
        Assert.Null(request.Scheme); Assert.Null(request.Credential);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("page-handle", body.RootElement.GetProperty("pageHandle").GetString());
        Assert.False(body.RootElement.TryGetProperty("userID", out _));
        Assert.Same(previous, Assert.Single(store.Writes));
    }

    [Theory]
    [InlineData(AuthenticationOperationPurpose.PasswordResetEmail, false)]
    [InlineData(AuthenticationOperationPurpose.PasswordResetManual, true)]
    [InlineData(AuthenticationOperationPurpose.EmailVerify, true)]
    public async Task Open_requires_same_purpose_and_unexpired_page_handle(AuthenticationOperationPurpose responsePurpose, bool future)
    {
        var transport = new ScriptedHttp(_ => Task.FromResult<AuthOutcome>(new SecurityLinkOpened("page", "s***@example.invalid",
            responsePurpose, DateTimeOffset.UtcNow.AddMinutes(future ? 5 : -1))));
        var flow = new AuthenticationFlow(transport.Client(), new RecordingStore());
        Assert.IsType<AuthenticationRefused>(await flow.OpenSecurityLinkAsync("grant", AuthenticationOperationPurpose.PasswordResetEmail));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_or_navigation_discards_a_late_completion_without_changing_sessions(bool verification)
    {
        var pending = new TaskCompletionSource<AuthOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingStore(); var previous = AuthenticationFlowTests.Session().Session;
        await store.StoreTokenAsync(previous);
        var transport = new ScriptedHttp(_ => pending.Task);
        var flow = new AuthenticationFlow(transport.Client(), store);
        var completing = Invoke(flow, verification ? "complete-verify" : "complete-reset");
        Assert.True(flow.Busy);
        Assert.IsType<AuthenticationRefused>(await flow.RequestPasswordResetAsync("synthetic"));
        flow.Restart(); pending.SetResult(verification ? new EmailVerificationCompleted() : new ReturnToLogin());
        Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await completing).Code);
        Assert.Same(previous, Assert.Single(store.Writes)); Assert.False(flow.Busy);
    }

    [Fact]
    public async Task Network_failure_refuses_without_removing_a_session()
    {
        var store = new RecordingStore(); await store.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var transport = new ScriptedHttp(_ => throw new HttpRequestException("Synthetic unavailable"));
        var flow = new AuthenticationFlow(transport.Client(), store);
        Assert.Equal(AuthenticationFailure.Unavailable, Assert.IsType<AuthenticationRefused>(await flow.CompletePasswordResetAsync("page", "password")).Code);
        Assert.Single(store.Writes); Assert.False(flow.Busy);
    }

    [Theory]
    [InlineData("#grant=opaque&purpose=PasswordResetEmail", false, true)]
    [InlineData("#grant=opaque&purpose=PasswordResetManual", false, true)]
    [InlineData("#grant=opaque&purpose=EmailVerify", true, true)]
    [InlineData("#grant=opaque&purpose=EmailVerify", false, false)]
    [InlineData("#grant=opaque&purpose=PasswordResetEmail", true, false)]
    [InlineData("#grant=opaque&purpose=7", false, false)]
    [InlineData("#grant=opaque&grant=second&purpose=PasswordResetEmail", false, false)]
    [InlineData("#grant=opaque&purpose=PasswordResetEmail&returnUrl=https://foreign.invalid", false, false)]
    [InlineData("#grant=bad%0A&purpose=PasswordResetEmail", false, false)]
    [InlineData("?grant=opaque&purpose=PasswordResetEmail", false, false)]
    public void Only_the_expected_single_fragment_grant_and_named_purpose_are_accepted(string suffix, bool verification, bool valid)
        => Assert.Equal(valid, SecurityLinkNavigation.TryRead("https://identity.invalid/Identity/ResetPassword" + suffix, verification, out _, out _));

    private static Task<AuthOutcome> Invoke(AuthenticationFlow flow, string action) => action switch
    {
        "request-reset" => flow.RequestPasswordResetAsync("synthetic"),
        "request-verify" => flow.RequestEmailVerificationAsync("synthetic"),
        "current" => flow.RequestCurrentEmailVerificationAsync("access"),
        "admin-reset" => flow.RequestAdminPasswordResetAsync("access", 42),
        "manual" => flow.RequestAdminPasswordResetAsync("access", 42, true),
        "admin-verify" => flow.RequestAdminEmailVerificationAsync("access", 42),
        "open" => flow.OpenSecurityLinkAsync("grant", AuthenticationOperationPurpose.PasswordResetEmail),
        "complete-reset" => flow.CompletePasswordResetAsync("page-handle", "Synthetic replacement password!"),
        _ => flow.CompleteEmailVerificationAsync("page-handle")
    };
}
