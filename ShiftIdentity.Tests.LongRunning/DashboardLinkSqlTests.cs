using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Services;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class DashboardLinkSqlTests(SqlIdentityFixture fixture) : IClassFixture<SqlIdentityFixture>, IAsyncLifetime
{
    private const string Destination = "saved+round2@example.invalid";
    private const string NewPassword = "Another synthetic phrase 29!";
    public async ValueTask InitializeAsync()
    {
        fixture.Clock = new ControlledClock(DateTimeOffset.UtcNow);
        await fixture.ResetAsync();
        await SetContact(fixture);
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task SetContact(SqlIdentityFixture target)
    {
        await using var db = target.CreateContext();
        var user = await db.Users.SingleAsync(x => x.ID == target.UserID);
        var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == user.ID);
        user.Email = Destination; user.EmailVerified = false; user.VerificationSASToken = "legacy-unchanged";
        state.EmailLookupKey = RecoveryContact.Key(Destination);
        RecoveryContact.RecordOwnership(user, state, RecoveryEmailProvenance.TrustedAdminAssignment);
        await db.SaveChangesAsync();
    }

    private static ConfiguredIdentityHttpHost<T> Host<T>(SqlIdentityFixture fixture, HostSecurityInbox inbox) where T : ShiftSoftware.ShiftIdentity.Data.ShiftIdentityDbContext =>
        new(fixture, c => { c.FrontEndUrl = "https://dashboard.example.invalid/"; c.EmailVerificationRedirectUrl = "https://hub.example.invalid/"; },
            configureServices: services =>
            {
                services.RemoveAll<ISecurityEmailSink>(); services.AddScoped<ISecurityEmailSink, HostSecurityEmailSink>();
                services.AddSingleton<ISendEmailVerification>(inbox); services.AddSingleton<ISendEmailResetPassword>(inbox);
            });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Configured_host_delivers_through_the_adapter_opens_inertly_and_completes_explicitly(bool verification)
    {
        var inbox = new HostSecurityInbox();
        using var host = Host<IdentityTestDbContext>(fixture, inbox);
        await Journey(fixture, host.Client, inbox, verification);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Link_admission_also_works_on_the_temporal_host_model(bool verification)
    {
        await using var temporal = new SqlIdentityFixture { Temporal = true, Clock = new ControlledClock(DateTimeOffset.UtcNow) };
        await temporal.InitializeAsync(); await temporal.ResetAsync(); await SetContact(temporal);
        var inbox = new HostSecurityInbox();
        using var host = Host<TemporalIdentityTestDbContext>(temporal, inbox);
        await Journey(temporal, host.Client, inbox, verification);
    }

    private static async Task Journey(SqlIdentityFixture target, HttpClient client, HostSecurityInbox inbox, bool verification)
    {
        using var login = await client.PostAsJsonAsync("api/Auth/Login", new { target.Username, target.Password });
        var old = (await login.Content.ReadFromJsonAsync<ShiftSoftware.ShiftEntity.Model.ShiftEntityResponse<TokenDTO>>())!.Entity!;
        var route = verification ? "email-verification" : "password-reset";
        using var request = await client.PostAsJsonAsync("api/identity/v2/" + route + "/request", new RequestSecurityEmail(target.Username));
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var message = Assert.Single(inbox.Messages);
        Assert.Equal(Destination, message.User.Email); Assert.Equal(target.Username, message.User.Username);
        Assert.Equal("https://dashboard.example.invalid/Identity/" + (verification ? "VerifyEmail" : "ResetPassword"), message.Link.Split('#')[0]);
        var grant = Uri.UnescapeDataString(message.Link.Split("#grant=")[1].Split('&')[0]);
        var purpose = verification ? AuthenticationOperationPurpose.EmailVerify : AuthenticationOperationPurpose.PasswordResetEmail;
        using var opened = await client.PostAsJsonAsync("api/identity/v2/security-link/open", new OpenSecurityLinkRequest(grant, purpose));
        var page = Assert.IsType<SecurityLinkOpened>(await opened.Content.ReadFromJsonAsync<AuthOutcome>());
        Assert.Equal("no-referrer", opened.Headers.GetValues("Referrer-Policy").Single());
        await using (var db = target.CreateContext())
        {
            var user = await db.Users.SingleAsync(x => x.ID == target.UserID);
            Assert.False(user.EmailVerified); Assert.Equal("legacy-unchanged", user.VerificationSASToken);
            Assert.True(HashService.VerifyVersionedPassword(target.Password, user.Salt, user.PasswordHash));
            Assert.Equal(1, (await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == user.ID)).SecurityVersion);
            Assert.Equal(AuthenticationOperationState.AwaitingExplicitSubmit, (await db.Set<AuthenticationOperation>().SingleAsync()).State);
        }
        using var completed = verification
            ? await client.PostAsJsonAsync("api/identity/v2/email-verification/complete?returnUrl=https://ignored.example.invalid", new CompleteEmailVerificationRequest(page.PageHandle))
            : await client.PostAsJsonAsync("api/identity/v2/password-reset/complete", new CompletePasswordResetRequest(page.PageHandle, NewPassword));
        var outcome = await completed.Content.ReadFromJsonAsync<AuthOutcome>();
        if (verification) Assert.Equal("https://hub.example.invalid/", Assert.IsType<EmailVerificationCompleted>(outcome).RedirectUrl);
        else Assert.IsType<ReturnToLogin>(outcome);
        using var replay = await client.PostAsJsonAsync("api/identity/v2/security-link/open", new OpenSecurityLinkRequest(grant, purpose));
        Assert.IsType<AuthenticationRefused>(await replay.Content.ReadFromJsonAsync<AuthOutcome>());
        using var refresh = await client.PostAsJsonAsync("api/Auth/Refresh", new { old.RefreshToken });
        Assert.Equal(verification ? HttpStatusCode.OK : HttpStatusCode.BadRequest, refresh.StatusCode);
        await using var check = target.CreateContext();
        var saved = await check.Users.SingleAsync(x => x.ID == target.UserID);
        Assert.True(saved.EmailVerified); Assert.Equal("legacy-unchanged", saved.VerificationSASToken);
        Assert.True(HashService.VerifyVersionedPassword(verification ? target.Password : NewPassword, saved.Salt, saved.PasswordHash));
        Assert.Equal(verification ? 1 : 2, (await check.Set<UserSecurityState>().SingleAsync(x => x.UserID == saved.ID)).SecurityVersion);
        Assert.DoesNotContain(await check.Set<AuthenticationAuditEvent>().Select(x => x.Outcome).ToListAsync(), x => x.Contains(grant));
    }

    [Fact]
    public async Task All_four_SAS_routes_refuse_under_the_authority_without_issuing_or_writing()
    {
        var inbox = new HostSecurityInbox();
        using var host = Host<IdentityTestDbContext>(fixture, inbox);
        using var login = await host.Client.PostAsJsonAsync("api/Auth/Login", new { fixture.Username, fixture.Password });
        var session = (await login.Content.ReadFromJsonAsync<ShiftSoftware.ShiftEntity.Model.ShiftEntityResponse<TokenDTO>>())!.Entity!;
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        string[] routes = ["SendEmailVerificationLink", "VerifyEmail/1?expires=1&token=legacy", "SendPasswordResetLink?email=saved%40example.invalid", "ResetPassword/1?expires=1&token=legacy"];
        foreach (var route in routes)
        {
            using var response = route.StartsWith("ResetPassword")
                ? await host.Client.PostAsJsonAsync("api/UserManager/" + route, new { NewPassword, ConfirmPassword = NewPassword })
                : await host.Client.GetAsync("api/UserManager/" + route);
            Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        }
        Assert.Empty(inbox.Messages);
        await using var db = fixture.CreateContext();
        Assert.Empty(await db.Set<AuthenticationOperation>().ToListAsync());
        var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
        Assert.False(user.EmailVerified); Assert.Equal("legacy-unchanged", user.VerificationSASToken);
        Assert.True(HashService.VerifyVersionedPassword(fixture.Password, user.Salt, user.PasswordHash));
    }

    [Theory]
    [InlineData(typeof(IUserAccountAuthority))]
    [InlineData(typeof(ISecurityEmailSink))]
    public void Configured_host_refuses_to_start_when_an_adapter_is_removed(Type missing)
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture,
            configureServices: services => services.RemoveAll(missing)));
        Assert.Contains(missing.Name, error.Message);
    }
}
