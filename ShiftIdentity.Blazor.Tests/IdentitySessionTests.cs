using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using ShiftSoftware.ShiftIdentity.Blazor;
using ShiftSoftware.ShiftIdentity.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Blazor.Handlers;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

[Trait("Category", "Ui")]
public sealed class IdentitySessionTests
{
    [Theory]
    [InlineData(false, -60)]
    [InlineData(true, -60)]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    public async Task Read_renews_expired_near_expiry_or_missing_access_once(bool admission, int seconds)
    {
        var renewed = Token("renewed");
        using var http = new RenewalHttp(admission, () => Task.FromResult(RenewalHttp.Success(admission, renewed)));
        using var host = new IdentitySessionTestHost(admission, http);
        var original = Token("original", seconds);
        if (seconds == 0) original.Token = "";
        await host.Storage.WriteAsync(original);
        Assert.Equal(renewed.Token, (await host.Session.GetTokenAsync())?.Token);
        Assert.Equal(renewed.Token, (await host.Session.GetTokenAsync())?.Token);
        Assert.Equal("renewed", host.Storage.Read()?.RefreshToken);
        Assert.Equal(1, http.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_access_and_empty_storage_do_not_renew(bool admission)
    {
        using var http = new RenewalHttp(admission, () => throw new InvalidOperationException("Unexpected renewal"));
        using var host = new IdentitySessionTestHost(admission, http);
        Assert.Null(await host.Session.GetTokenAsync());
        var token = Token("current");
        await host.Session.StoreTokenAsync(token);
        Assert.Same(token, await host.Session.GetTokenAsync());
        Assert.Equal(0, http.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_reads_share_one_renewal_and_all_receive_new_access(bool admission)
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new RenewalHttp(admission, () => pending.Task);
        using var host = new IdentitySessionTestHost(admission, http);
        await host.Storage.WriteAsync(Token("expired", -60));
        var reads = Enumerable.Range(0, 32).Select(_ => Task.Run(host.Session.GetTokenAsync)).ToArray();
        await http.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var renewed = Token("renewed");
        pending.SetResult(RenewalHttp.Success(admission, renewed));
        var results = await Task.WhenAll(reads);
        Assert.All(results, token => Assert.Equal(renewed.Token, token?.Token));
        Assert.Equal(1, http.Calls);
    }

    [Theory]
    [InlineData(false, AuthPurpose.None, false)]
    [InlineData(true, AuthPurpose.None, false)]
    [InlineData(false, AuthPurpose.Mfa, true)]
    [InlineData(true, AuthPurpose.Mfa, true)]
    [InlineData(false, AuthPurpose.MfaEnrollment, false)]
    [InlineData(true, AuthPurpose.MfaEnrollment, false)]
    [InlineData(false, AuthPurpose.ChangePassword, true)]
    [InlineData(true, AuthPurpose.ChangePassword, true)]
    public async Task Restricted_or_nonrenewable_token_is_returned_as_is_without_refresh(bool admission, AuthPurpose purpose, bool refresh)
    {
        using var http = new RenewalHttp(admission, () => throw new InvalidOperationException("Unexpected renewal"));
        using var host = new IdentitySessionTestHost(admission, http);
        var token = Token("temporary", -60); token.Flow = purpose;
        if (!refresh) token.RefreshToken = "";
        // v2 flows reject these on StoreTokenAsync; persisted restricted data must never be renewed either.
        await host.Storage.WriteAsync(token);
        Assert.Same(token, await host.Session.GetTokenAsync());
        Assert.False(await host.Session.RenewAsync());
        Assert.Equal(0, http.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refused_refresh_returns_null_and_only_v2_removes_storage(bool admission)
    {
        using var http = new RenewalHttp(admission, () => Task.FromResult(RenewalHttp.Refused(admission)));
        using var host = new IdentitySessionTestHost(admission, http);
        var token = Token("expired", -60); await host.Storage.WriteAsync(token);
        Assert.Null(await host.Session.GetTokenAsync());
        if (admission) Assert.Null(host.Storage.Read());
        else Assert.Same(token, host.Storage.Read());
        Assert.Equal(1, http.Calls);
    }

    [Theory]
    [InlineData("unavailable", false)]
    [InlineData("unavailable", true)]
    [InlineData("network", false)]
    [InlineData("network", true)]
    [InlineData("timeout", false)]
    [InlineData("timeout", true)]
    [InlineData("json", false)]
    [InlineData("json", true)]
    public async Task V2_unavailable_keeps_credentials_and_only_returns_current_access(string failure, bool expired)
    {
        using var http = new RenewalHttp(true, () => failure switch
        {
            "network" => throw new HttpRequestException("Synthetic failure"),
            "timeout" => throw new TaskCanceledException("Synthetic timeout"),
            "json" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("not JSON") }),
            _ => Task.FromResult(RenewalHttp.Refused(true, AuthenticationFailure.Unavailable))
        });
        using var host = new IdentitySessionTestHost(true, http);
        var token = Token("original", expired ? -60 : 600); await host.Storage.WriteAsync(token);
        Assert.False(await host.Session.RenewAsync());
        Assert.Same(token, host.Storage.Read());
        Assert.Equal(expired ? null : token.Token, (await host.Session.GetTokenAsync())?.Token);
    }

    [Fact]
    public async Task Legacy_transport_failure_propagates_and_keeps_storage()
    {
        using var http = new RenewalHttp(false, () => throw new HttpRequestException("Synthetic failure"));
        using var host = new IdentitySessionTestHost(false, http);
        var token = Token("expired", -60); await host.Storage.WriteAsync(token);
        await Assert.ThrowsAsync<HttpRequestException>(() => host.Session.GetTokenAsync());
        Assert.Same(token, host.Storage.Read());
    }

    public static TheoryData<bool, bool, string> RenewalRaces => new(
        from admission in new[] { false, true }
        from refused in new[] { false, true }
        from action in new[] { "logout", "login", "same-credentials-login", "another-tab-refresh" }
        select (admission, refused, action));

    [Theory]
    [MemberData(nameof(RenewalRaces))]
    public async Task Logout_or_new_login_wins_over_inflight_renewal(bool admission, bool refused, string action)
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new RenewalHttp(admission, () => pending.Task);
        using var host = new IdentitySessionTestHost(admission, http);
        var original = Token("original", 8);
        await host.Session.StoreTokenAsync(original);
        var reading = host.Session.GetTokenAsync();
        await http.Started.Task;
        TokenDTO? newer = null;
        if (action == "logout") await host.Session.RemoveTokenAsync();
        else
        {
            newer = action == "same-credentials-login" ? original : Token("new-login");
            if (action == "another-tab-refresh")
            {
                newer.Token = original.Token;
                await host.Storage.WriteAsync(newer);
            }
            else await host.Session.StoreTokenAsync(newer);
        }
        pending.SetResult(refused ? RenewalHttp.Refused(admission) : RenewalHttp.Success(admission, Token("late")));
        Assert.Equal(newer?.RefreshToken, (await reading)?.RefreshToken);
        Assert.Same(newer, host.Storage.Read());
        Assert.Equal(1, http.Calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Cookie_session_from_sibling_app_renews_with_missing_or_empty_local_access(bool admission, bool emptyLocal)
    {
        using var http = new RenewalHttp(admission, () => Task.FromResult(RenewalHttp.Success(admission, Token("renewed"))));
        using var host = new IdentitySessionTestHost(admission, http, browser: true, cookie: true);
        host.Browser.RefreshCookie = "sibling-refresh";
        if (emptyLocal) await host.Storage.WriteAsync(new TokenDTO { Token = "" });
        Assert.Equal("renewed", (await host.Session.GetTokenAsync())?.RefreshToken);
        Assert.Equal("renewed", host.Browser.RefreshCookie);
        Assert.Equal(1, http.Calls);
        Assert.Contains("domain=.example.invalid;", Assert.Single(host.Browser.CookieWrites));
        Assert.Contains("path=/;", host.Browser.CookieWrites[0]);
        Assert.Contains("max-age=3600;", host.Browser.CookieWrites[0]);
    }

    [Fact]
    public async Task Cookie_step_up_keeps_sibling_cookie_and_logout_removes_both_parts()
    {
        using var http = new RenewalHttp(false, () => throw new InvalidOperationException("Unexpected renewal"));
        using var host = new IdentitySessionTestHost(false, http, browser: true, cookie: true);
        host.Browser.RefreshCookie = "sibling-refresh";
        var token = Token("step", -60); token.Flow = AuthPurpose.Mfa; token.RefreshToken = "";
        await host.Session.StoreTokenAsync(token);
        Assert.Equal(token.Token, (await host.Session.GetTokenAsync())?.Token);
        Assert.Equal("sibling-refresh", host.Browser.RefreshCookie);
        Assert.Empty(host.Browser.CookieWrites);
        Assert.Equal(0, http.Calls);
        await host.Session.RemoveTokenAsync();
        Assert.Null(host.Browser.RefreshCookie); Assert.Empty(host.Browser.Local);
        Assert.Contains("domain=.example.invalid;", Assert.Single(host.Browser.CookieWrites));
    }

    [Fact]
    public async Task Expired_cookie_cannot_use_the_refresh_copy_in_local_storage()
    {
        using var http = new RenewalHttp(false, () => throw new InvalidOperationException("Unexpected renewal"));
        using var host = new IdentitySessionTestHost(false, http, browser: true, cookie: true);
        await host.Session.StoreTokenAsync(Token("original", -60));
        host.Browser.RefreshCookie = null;
        Assert.Null((await host.Session.GetTokenAsync())?.RefreshToken);
        Assert.Equal(0, http.Calls);
    }

    [Fact]
    public async Task Local_storage_registration_persists_reads_and_removes_the_token()
    {
        using var http = new RenewalHttp(false, () => Task.FromResult(RenewalHttp.Success(false, Token("renewed"))));
        using var host = new IdentitySessionTestHost(false, http, browser: true);
        await host.Session.StoreTokenAsync(Token("expired", -60));
        Assert.Equal("renewed", (await host.Session.GetTokenAsync())?.RefreshToken);
        Assert.Equal(host.Storage.Read()?.Token, host.Session.GetToken());
        Assert.Single(host.Browser.Local); Assert.Empty(host.Browser.CookieWrites);
        await host.Session.RemoveTokenAsync();
        Assert.Empty(host.Browser.Local);
    }

    [Theory]
    [InlineData("purpose")]
    [InlineData("schema")]
    [InlineData("lifetime")]
    [InlineData("expired")]
    [InlineData("refresh")]
    [InlineData("malformed")]
    public async Task V2_rejects_invalid_stores_and_renewal_payloads(string problem)
    {
        var invalid = Token("invalid", problem == "expired" ? -60 : 600,
            problem == "schema" ? "1" : "2", problem == "purpose" ? "operation" : "access");
        if (problem == "lifetime") invalid.TokenLifeTimeInSeconds = 901;
        if (problem == "refresh") invalid.RefreshToken = "";
        if (problem == "malformed") invalid.Token = "opaque-operation";
        using var http = new RenewalHttp(true, () => Task.FromResult(RenewalHttp.Success(true, invalid)));
        using var host = new IdentitySessionTestHost(true, http);
        await Assert.ThrowsAsync<ArgumentException>(() => host.Session.StoreTokenAsync(invalid));
        var original = Token("original", -60); await host.Storage.WriteAsync(original);
        Assert.Null(await host.Session.GetTokenAsync());
        Assert.Same(original, host.Storage.Read());
    }

    [Fact]
    public async Task Legacy_dead_session_401_still_removes_token_without_auth_notification()
    {
        using var http = new RenewalHttp(false, () => Task.FromResult(RenewalHttp.Refused(false)));
        using var host = new IdentitySessionTestHost(false, http);
        var notifications = 0; host.Auth.AuthenticationStateChanged += _ => notifications++;
        await host.Session.StoreTokenAsync(Token("expired", -60));
        using var context = new Bunit.BunitContext(); context.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        var messages = new MessageService(context.JSInterop.JSRuntime, host.Session);
        using var handler = new TokenMessageHandlerWithAutoRefresh(host.Session, messages) { InnerHandler = new UnauthorizedResource() };
        using var client = new HttpClient(handler);
        using var response = await client.GetAsync("https://resource.invalid/data", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(host.Storage.Read()); Assert.Equal(0, notifications);
        Assert.NotEmpty(context.JSInterop.Invocations);
    }

    [Fact]
    public void Session_is_sealed_with_only_the_four_public_operations_and_no_public_constructor()
    {
        Assert.True(typeof(IdentitySession).IsSealed);
        Assert.Empty(typeof(IdentitySession).GetConstructors());
        Assert.Equal(new[] { "GetToken", "GetTokenAsync", "RemoveTokenAsync", "StoreTokenAsync" },
            typeof(IdentitySession).GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
                .Select(m => m.Name).Order().ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_refused_reads_share_the_same_failure(bool admission)
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new RenewalHttp(admission, () => pending.Task);
        using var host = new IdentitySessionTestHost(admission, http);
        await host.Storage.WriteAsync(Token("expired", -60));
        var reads = Enumerable.Range(0, 16).Select(_ => host.Session.GetTokenAsync()).ToArray();
        Assert.Equal(1, http.Calls);
        pending.SetResult(RenewalHttp.Refused(admission));
        Assert.All(await Task.WhenAll(reads), Assert.Null);
        Assert.Equal(1, http.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Logout_wins_when_renewal_is_already_writing_to_slow_storage(bool admission)
    {
        var storage = new SlowStorage();
        using var http = new RenewalHttp(admission, () => Task.FromResult(RenewalHttp.Success(admission, Token("renewed"))));
        using var host = new IdentitySessionTestHost(admission, http, storage);
        var reading = host.Session.GetTokenAsync();
        await storage.Writing.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var logout = host.Session.RemoveTokenAsync();
        storage.AllowWrite.SetResult();
        await Task.WhenAll(reading, logout);
        Assert.Null(storage.Read());
        Assert.Null(await host.Session.GetTokenAsync());
    }

    [Fact]
    public async Task Preview_registration_isolates_credentials_between_server_circuits()
    {
        var registrations = new ServiceCollection();
        registrations.AddLogging();
        registrations.AddIdentityAdmissionSession(_ => new HttpClient());
        using var provider = registrations.BuildServiceProvider();
        using var first = provider.CreateScope(); using var second = provider.CreateScope();
        var session = first.ServiceProvider.GetRequiredService<IdentitySession>();
        await session.StoreTokenAsync(Token("first-circuit"));
        Assert.Equal("first-circuit", (await session.GetTokenAsync())?.RefreshToken);
        Assert.Null(await second.ServiceProvider.GetRequiredService<IdentitySession>().GetTokenAsync());
    }

    [Fact]
    public async Task Development_registration_uses_its_own_key_and_removes_malformed_storage()
    {
        var browser = new BrowserStorageRuntime();
        var registrations = new ServiceCollection();
        registrations.AddLogging(); registrations.AddSingleton<IJSRuntime>(browser);
        registrations.AddShiftIdentity("synthetic-app", "https://identity.invalid/", "https://client.invalid/");
        registrations.AddIdentityAdmissionSession(_ => new HttpClient(), "identity-development-run");
        using var provider = registrations.BuildServiceProvider();
        var session = provider.GetRequiredService<IdentitySession>();
        browser.Local["token"] = "production storage is separate";
        await session.StoreTokenAsync(Token("development"));
        Assert.Equal("development", (await session.GetTokenAsync())?.RefreshToken);
        browser.Local["identity-development-run"] = "broken JSON";
        Assert.Null(await session.GetTokenAsync());
        Assert.Equal("production storage is separate", Assert.Single(browser.Local).Value);
    }

    private sealed class SlowStorage : IIdentityTokenStorage
    {
        private TokenDTO? token = Token("expired", -60);
        public TaskCompletionSource Writing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TokenDTO? Read() => token;
        public Task<TokenDTO?> ReadAsync() => Task.FromResult(token);
        public async Task WriteAsync(TokenDTO value)
        {
            Writing.TrySetResult();
            await AllowWrite.Task;
            token = value;
        }
        public Task RemoveAsync() { token = null; return Task.CompletedTask; }
    }

    private sealed class UnauthorizedResource : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.True(string.IsNullOrWhiteSpace(request.Headers.Authorization?.Parameter));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }
    }

    internal static TokenDTO Token(string id, int seconds = 600, string schema = "2", string purpose = "access")
    {
        string Encode(object value) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new()
        {
            Token = Encode(new { alg = "none" }) + "." + Encode(new { shift_schema = schema, shift_purpose = purpose,
                exp = DateTimeOffset.UtcNow.AddSeconds(seconds).ToUnixTimeSeconds(), jti = id }) + ".",
            RefreshToken = id, TokenLifeTimeInSeconds = 600, RefreshTokenLifeTimeInSeconds = 3600
        };
    }
}
