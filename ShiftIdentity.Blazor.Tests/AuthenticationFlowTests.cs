using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ShiftSoftware.ShiftIdentity.Blazor;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

[Trait("Category", "Ui")]
public sealed class AuthenticationFlowTests
{
    [Theory]
    [InlineData(AuthenticationStep.ExistingMfa)]
    [InlineData(AuthenticationStep.PasswordChange)]
    [InlineData(AuthenticationStep.NewMfa)]
    [InlineData(AuthenticationStep.MfaRecovery)]
    [InlineData(AuthenticationStep.EmailVerification)]
    public async Task Restricted_outcomes_never_enter_session_storage(AuthenticationStep step)
    {
        var store = new RecordingStore();
        var transport = new ScriptedHttp(_ => Task.FromResult<AuthOutcome>(Challenge(step)));
        var flow = new AuthenticationFlow(transport.Client(), store);
        Assert.IsType<ChallengeRequired>(await flow.LoginAsync("synthetic", "password"));
        Assert.Empty(store.Writes);
        Assert.NotNull(flow.Pending);
        flow.Restart();
        Assert.Null(flow.Pending);
        Assert.IsType<AuthenticationRefused>(await flow.CompleteMfaAsync("123456"));
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task Completion_sends_bound_operation_credentials_and_stores_only_session()
    {
        var store = new RecordingStore();
        var transport = new ScriptedHttp(request => Task.FromResult<AuthOutcome>(
            request.RequestUri!.AbsolutePath.EndsWith("/mfa") ? Session() : Challenge()));
        var flow = new AuthenticationFlow(transport.Client(), store);
        await flow.LoginAsync("synthetic", "password");
        Assert.Empty(store.Writes);
        Assert.IsType<SessionIssued>(await flow.CompleteMfaAsync("123456"));
        Assert.Single(store.Writes);
        Assert.Null(flow.Pending);
        var login = JsonSerializer.Deserialize<PasswordLoginRequest>(transport.Requests[0].Body, ScriptedHttp.Json)!;
        var complete = JsonSerializer.Deserialize<CompleteMfaRequest>(transport.Requests[1].Body, ScriptedHttp.Json)!;
        Assert.Equal("Operation", transport.Requests[1].Scheme);
        Assert.Equal("opaque-operation", transport.Requests[1].Credential);
        Assert.Equal(login.CodeChallenge, Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
            Encoding.ASCII.GetBytes(complete.CodeVerifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
        Assert.DoesNotContain("opaque-operation", JsonSerializer.Serialize(store.Writes));
        Assert.DoesNotContain(complete.CodeVerifier, JsonSerializer.Serialize(store.Writes));
    }

    [Fact]
    public async Task Restart_and_duplicate_submit_cannot_store_a_late_response()
    {
        var pending = new TaskCompletionSource<AuthOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingStore();
        var transport = new ScriptedHttp(_ => pending.Task);
        var flow = new AuthenticationFlow(transport.Client(), store);
        var first = flow.LoginAsync("synthetic", "password");
        Assert.IsType<AuthenticationRefused>(await flow.LoginAsync("synthetic", "password"));
        flow.Restart();
        pending.SetResult(Session());
        Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await first).Code);
        Assert.Empty(store.Writes);
        Assert.Single(transport.Requests);
    }

    [Theory]
    [InlineData("temporary")]
    [InlineData("operation")]
    [InlineData("missing-refresh")]
    [InlineData("network")]
    [InlineData("expired")]
    public async Task Invalid_or_unavailable_results_never_write_credentials(string scenario)
    {
        var store = new RecordingStore();
        var transport = new ScriptedHttp(_ =>
        {
            if (scenario == "network") throw new HttpRequestException("Synthetic transport failure");
            if (scenario == "expired") return Task.FromResult<AuthOutcome>(new ChallengeRequired(
                new(AuthenticationStep.ExistingMfa, "opaque", DateTimeOffset.UtcNow.AddMinutes(-1))));
            var session = Session();
            if (scenario == "temporary") session.Session.Flow = ShiftSoftware.ShiftIdentity.Core.Enums.AuthPurpose.Mfa;
            if (scenario == "operation") session.Session.Token = "opaque-operation";
            if (scenario == "missing-refresh") session.Session.RefreshToken = "";
            return Task.FromResult<AuthOutcome>(session);
        });
        var flow = new AuthenticationFlow(transport.Client(), store);
        Assert.IsType<AuthenticationRefused>(await flow.LoginAsync("synthetic", "password"));
        Assert.Empty(store.Writes);
        Assert.Null(flow.Pending);
    }

    public static ChallengeRequired Challenge(AuthenticationStep step = AuthenticationStep.ExistingMfa) =>
        new(new(step, step == AuthenticationStep.ExistingMfa ? "opaque-operation" : null, DateTimeOffset.UtcNow.AddMinutes(5)));

    [Fact]
    public async Task Voluntary_flow_binds_fresh_password_MFA_and_new_password_to_rotated_handles()
    {
        var store = new RecordingStore(); var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
        var index = 0;
        var transport = new ScriptedHttp(async request =>
        {
            index++;
            if (index == 1)
            {
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal("current-access", request.Headers.Authorization?.Parameter);
                return new ChallengeRequired(new(AuthenticationStep.Password, "first", deadline, AuthenticationOperationPurpose.PasswordChange));
            }
            Assert.Equal("Operation", request.Headers.Authorization?.Scheme);
            if (index == 2)
            {
                Assert.Equal("first", request.Headers.Authorization?.Parameter);
                var proof = await request.Content!.ReadFromJsonAsync<PasswordChangeProofRequest>(); Assert.Equal("current password", proof!.CurrentPassword);
                return new ChallengeRequired(new(AuthenticationStep.ExistingMfa, "second", deadline, AuthenticationOperationPurpose.PasswordChange));
            }
            if (index == 3)
            {
                Assert.EndsWith("password-change/mfa", request.RequestUri!.AbsolutePath);
                Assert.Equal("second", request.Headers.Authorization?.Parameter);
                return new ChallengeRequired(new(AuthenticationStep.PasswordChange, "third", deadline, AuthenticationOperationPurpose.PasswordChange));
            }
            Assert.Equal("third", request.Headers.Authorization?.Parameter);
            return index == 4 ? new AuthenticationRefused(AuthenticationFailure.InvalidNewPassword, PasswordPolicyFailure.TooShort)
                : new PasswordChanged(Session());
        });
        var flow = new AuthenticationFlow(transport.Client(), store);
        await flow.BeginPasswordChangeAsync("current-access"); await flow.ProvePasswordAsync("current password");
        await flow.CompleteMfaAsync("123456"); Assert.Empty(store.Writes);
        Assert.IsType<AuthenticationRefused>(await flow.ChangePasswordAsync("short")); Assert.NotNull(flow.Pending);
        Assert.IsType<PasswordChanged>(await flow.ChangePasswordAsync("A long new synthetic password"));
        Assert.Single(store.Writes); Assert.Null(flow.Pending); Assert.True(flow.PasswordWasChanged);
        var values = transport.Requests.Skip(1).Select(x => JsonDocument.Parse(x.Body).RootElement.GetProperty("codeVerifier").GetString()).ToArray();
        Assert.All(values, x => Assert.Equal(values[0], x));
    }

    [Fact]
    public async Task Changed_but_restricted_response_never_stores_a_session()
    {
        var store = new RecordingStore();
        var flow = new AuthenticationFlow(new ScriptedHttp(_ => Task.FromResult<AuthOutcome>(new PasswordChanged(Challenge(AuthenticationStep.EmailVerification)))).Client(), store);
        Assert.IsType<PasswordChanged>(await flow.LoginAsync("synthetic", "password"));
        Assert.True(flow.PasswordWasChanged); Assert.Empty(store.Writes); Assert.NotNull(flow.Pending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_during_submission_never_stores_late_credentials_and_revokes_rotated_challenge(bool intermediate)
    {
        var pending = new TaskCompletionSource<AuthOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingStore(); var calls = 0;
        var transport = new ScriptedHttp(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/cancel")) return Task.FromResult<AuthOutcome>(new OperationCancelled());
            return ++calls == 1 ? Task.FromResult<AuthOutcome>(new ChallengeRequired(new(AuthenticationStep.PasswordChange, "first", DateTimeOffset.UtcNow.AddMinutes(5), AuthenticationOperationPurpose.PasswordChange))) : pending.Task;
        });
        var flow = new AuthenticationFlow(transport.Client(), store);
        await flow.LoginAsync("synthetic", "password");
        var changing = flow.ChangePasswordAsync("A long new synthetic password");
        Assert.IsType<OperationCancelled>(await flow.CancelAsync());
        pending.SetResult(intermediate ? new ChallengeRequired(new(AuthenticationStep.ExistingMfa, "rotated", DateTimeOffset.UtcNow.AddMinutes(5), AuthenticationOperationPurpose.PasswordChange)) : new PasswordChanged(Session()));
        Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await changing).Code);
        Assert.Empty(store.Writes); Assert.Null(flow.Pending);
        if (intermediate) Assert.Equal("rotated", transport.Requests.Last().Credential);
    }

    // Synthetic unsigned wire fixture for client structural tests. Server signature checks have separate coverage.
    public static SessionIssued Session()
    {
        string Encode(object value) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new(new TokenDTO {
            Token = Encode(new { alg = "none" }) + "." + Encode(new { shift_purpose = "access", shift_schema = "2",
                exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds() }) + ".",
            RefreshToken = "synthetic-refresh", TokenLifeTimeInSeconds = 600,
            UserData = new() { ID = "42", Username = "synthetic", FullName = "Synthetic User" }
        });
    }
}

public sealed class RecordingStore : IIdentityStore
{
    public List<TokenDTO> Writes { get; } = [];
    public Task<TokenDTO?> GetTokenAsync() => Task.FromResult(Writes.LastOrDefault());
    public string? GetToken() => Writes.LastOrDefault()?.Token;
    public Task StoreTokenAsync(TokenDTO token) { Writes.Add(token); return Task.CompletedTask; }
    public Task RemoveTokenAsync() { Writes.Clear(); return Task.CompletedTask; }
}

public sealed class ScriptedHttp(Func<HttpRequestMessage, Task<AuthOutcome>> respond) : HttpMessageHandler
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);
    public List<(string? Scheme, string? Credential, string Body)> Requests { get; } = [];
    public HttpClient Client() => new(this) { BaseAddress = new("https://identity.invalid/") };
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request.Headers.Authorization?.Scheme, request.Headers.Authorization?.Parameter,
            await request.Content!.ReadAsStringAsync(cancellationToken)));
        return new(HttpStatusCode.OK) { Content = JsonContent.Create<AuthOutcome>(await respond(request)) };
    }
}
