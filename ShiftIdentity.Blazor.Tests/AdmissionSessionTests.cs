using Blazored.LocalStorage;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Blazor;
using ShiftSoftware.ShiftIdentity.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

[Trait("Category", "Ui")]
public sealed class AdmissionSessionTests
{
    [Theory]
    [InlineData("logout", false)]
    [InlineData("logout", true)]
    [InlineData("credential-change", false)]
    [InlineData("credential-change", true)]
    [InlineData("another-tab", false)]
    [InlineData("another-tab", true)]
    public async Task Renewal_cannot_overwrite_a_newer_session_or_logout(string action, bool refused)
    {
        var response = new TaskCompletionSource<AuthOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new ScriptedHttp(_ => response.Task);
        var storage = new MemoryStorage();
        using var host = new IdentitySessionTestHost(true, transport, storage);
        var store = host.Session;
        await store.StoreTokenAsync(Session("original"));
        var renewing = store.RenewAsync();
        if (action == "logout") await store.RemoveTokenAsync();
        else if (action == "credential-change") await store.StoreTokenAsync(Session("changed"));
        else storage.SetItem("test", Session("changed"));
        response.SetResult(refused ? new AuthenticationRefused(AuthenticationFailure.StaleOperation) : new SessionIssued(Session("late")));
        await renewing;
        Assert.Equal(action == "logout" ? null : "changed", (await store.GetTokenAsync())?.RefreshToken);
    }

    [Theory]
    [InlineData(AuthPurpose.Mfa)]
    [InlineData(AuthPurpose.MfaEnrollment)]
    [InlineData(AuthPurpose.ChangePassword)]
    public async Task Session_store_rejects_temporary_credentials(AuthPurpose purpose)
    {
        using var host = new IdentitySessionTestHost(true, new ScriptedHttp(_ => throw new InvalidOperationException()));
        var store = host.Session;
        var token = Session("temporary"); token.Flow = purpose;
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreTokenAsync(token));
        Assert.Null(store.GetToken());
    }

    [Theory]
    [InlineData(AuthenticationFailure.StaleOperation, false)]
    [InlineData(AuthenticationFailure.InvalidGrant, false)]
    [InlineData(AuthenticationFailure.Unavailable, true)]
    public async Task Renewal_refusal_clears_authentication_but_transport_unavailability_preserves_current_access(AuthenticationFailure failure, bool retained)
    {
        var transport = new ScriptedHttp(request =>
        {
            Assert.Equal("/api/identity/v2/refresh", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult<AuthOutcome>(new AuthenticationRefused(failure));
        });
        using var host = new IdentitySessionTestHost(true, transport);
        var store = host.Session;
        var notifications = 0;
        host.Auth.AuthenticationStateChanged += _ => notifications++;
        await store.StoreTokenAsync(Session("original"));
        Assert.False(await store.RenewAsync());
        Assert.Equal(retained ? 1 : 2, notifications);
        Assert.Equal(retained, (await host.Auth.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task Concurrent_renewal_uses_one_request_and_notifies_auth_state()
    {
        var response = new TaskCompletionSource<AuthOutcome>();
        var transport = new ScriptedHttp(_ => response.Task);
        using var host = new IdentitySessionTestHost(true, transport);
        var store = host.Session;
        await store.StoreTokenAsync(Session("original"));
        var first = store.RenewAsync(); var second = store.RenewAsync();
        Assert.Single(transport.Requests);
        response.SetResult(new SessionIssued(Session("renewed")));
        Assert.True(await first); Assert.True(await second);
        Assert.Equal("renewed", (await store.GetTokenAsync())!.RefreshToken);
    }

    [Theory]
    [InlineData("Identity/UserDataForm", "/Identity/UserDataForm")]
    [InlineData("/Identity/UserDataForm", "/Identity/UserDataForm")]
    [InlineData("https://other.invalid/path", "/")]
    [InlineData("//other.invalid/path", "/")]
    [InlineData("\\other.invalid", "/")]
    [InlineData("javascript:alert(1)", "/")]
    public void Login_return_paths_stay_local(string value, string expected) => Assert.Equal(expected, AdmissionNavigation.LocalReturnPath(value));

    private static TokenDTO Session(string id)
    {
        var session = AuthenticationFlowTests.Session().Session;
        session.RefreshToken = id;
        // Synthetic unsigned wire token: only structural client checks are under test here.
        session.Token = session.Token[..session.Token.LastIndexOf('.')] + "." + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(id)).TrimEnd('=');
        return session;
    }
}

internal sealed class MemoryStorage : ISyncLocalStorageService, IIdentityTokenStorage
{
    public TokenDTO? Read() => GetItem<TokenDTO>("test");
    public Task<TokenDTO?> ReadAsync() => Task.FromResult(Read());
    public Task WriteAsync(TokenDTO token) { SetItem("test", token); return Task.CompletedTask; }
    public Task RemoveAsync() { RemoveItem("test"); return Task.CompletedTask; }
    private readonly Dictionary<string, string> values = [];
    public event EventHandler<ChangingEventArgs>? Changing;
    public event EventHandler<ChangedEventArgs>? Changed;
    public void Clear() => values.Clear();
    public bool ContainKey(string key) => values.ContainsKey(key);
    public T GetItem<T>(string key) => values.TryGetValue(key, out var value) ? System.Text.Json.JsonSerializer.Deserialize<T>(value)! : default!;
    public string GetItemAsString(string key) => values.GetValueOrDefault(key)!;
    public string Key(int index) => values.Keys.ElementAt(index);
    public IEnumerable<string> Keys() => values.Keys;
    public int Length() => values.Count;
    public void RemoveItem(string key) => values.Remove(key);
    public void RemoveItems(IEnumerable<string> keys) { foreach (var key in keys) RemoveItem(key); }
    public void SetItem<T>(string key, T value) => SetItemAsString(key, System.Text.Json.JsonSerializer.Serialize(value));
    public void SetItemAsString(string key, string value)
    {
        var before = GetItemAsString(key);
        var change = new ChangingEventArgs { Key = key, OldValue = before, NewValue = value };
        Changing?.Invoke(this, change); if (change.Cancel) return;
        values[key] = value; Changed?.Invoke(this, new ChangedEventArgs { Key = key, OldValue = before, NewValue = value });
    }
}
