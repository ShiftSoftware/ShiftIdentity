using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// State and helpers shared by the security-link classes. Each class owns a database through its own
/// SqlIdentityFixture, so xUnit runs the classes in parallel; inside a class the tests still run one at a time, and
/// every test starts from a reset account that has a saved, eligible address.
/// </summary>
public abstract class SecurityLinkTestBase : IAsyncLifetime
{
    protected readonly SqlIdentityFixture fixture;
    protected readonly string Email = "saved+reset-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    protected const string NewPassword = "Another synthetic password 83!";
    protected ControlledClock clock = null!;

    protected SecurityLinkTestBase(SqlIdentityFixture fixture) => this.fixture = fixture;

    public async ValueTask InitializeAsync()
    {
        clock = new(DateTimeOffset.UtcNow); fixture.Clock = clock;
        await fixture.ResetAsync();
        await Contact(Email, true);
    }
    public ValueTask DisposeAsync() { fixture.Clock = TimeProvider.System; return ValueTask.CompletedTask; }

    protected async Task Contact(string? email, bool eligible, bool verified = false, long? userID = null)
    {
        await using var db = fixture.CreateContext();
        var id = userID ?? fixture.UserID;
        var user = await db.Users.SingleAsync(x => x.ID == id); var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == id);
        user.Email = email; user.EmailVerified = verified; RecoveryContact.Invalidate(state);
        state.UsernameLookupKey = null; state.EmailLookupKey = null; RecoveryContact.InitializeLookup(user, state);
        if (eligible) RecoveryContact.RecordOwnership(user, state, RecoveryEmailProvenance.TrustedAdminAssignment);
        await db.SaveChangesAsync();
    }
    protected static async Task<AuthOutcome> Request(IdentityHttpHost host, string identifier, bool verification = false) =>
        await Post(host, (verification ? "email-verification" : "password-reset") + "/request", new RequestSecurityEmail(identifier));
    protected static Task<AuthOutcome> Open(IdentityHttpHost host, string grant, AuthenticationOperationPurpose purpose) => Post(host, "security-link/open", new OpenSecurityLinkRequest(grant, purpose));
    protected static Task<AuthOutcome> Reset(IdentityHttpHost host, string page, string password = NewPassword) => Post(host, "password-reset/complete", new CompletePasswordResetRequest(page, password));
    protected static Task<AuthOutcome> Verify(IdentityHttpHost host, string page) => Post(host, "email-verification/complete", new CompleteEmailVerificationRequest(page));
    protected static async Task<AuthOutcome> Post<T>(IdentityHttpHost host, string path, T payload, string? access = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/v2/" + path) { Content = JsonContent.Create(payload) };
        if (access is not null) request.Headers.Authorization = new("Bearer", access);
        return await IdentityHttpHost.Read(await host.Client.SendAsync(request));
    }
    protected LocalSecurityInbox Inbox => Assert.IsType<LocalSecurityInbox>(fixture.EmailSink);
    protected static Task<string> Grant(IdentityHttpHost host)
    {
        var inbox = Assert.IsType<LocalSecurityInbox>(host.Services.GetRequiredService<ISecurityEmailSink>());
        var link = inbox.Messages.First().Link;
        return Task.FromResult(Uri.UnescapeDataString(link.Split("#grant=", 2)[1].Split('&', 2)[0]));
    }
    protected async Task<UserSecurityState> State()
    { await using var db = fixture.CreateContext(); return await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == fixture.UserID); }
    protected async Task AssertPassword(string password, long version, bool verified)
    {
        await using var db = fixture.CreateContext(); var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
        Assert.True(HashService.VerifyVersionedPassword(password, user.Salt, user.PasswordHash)); Assert.Equal(verified, user.EmailVerified);
        Assert.Equal(version, (await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID)).SecurityVersion);
    }
    protected string Code() => new Totp(fixture.FactorSecret).ComputeTotp(clock.GetUtcNow().UtcDateTime);
}
