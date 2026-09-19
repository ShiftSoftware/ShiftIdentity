using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class AdministratorConfirmationSqlTests(SqlIdentityFixture fixture) : IClassFixture<SqlIdentityFixture>
{
    private const string Write = "{\"ShiftIdentityActions\":{\"Users\":[\"w\",\"d\"]}}";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Old_session_confirms_same_operator_and_continues_once_with_current_password_and_applicable_mfa(bool mfa)
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow); fixture.Clock = clock;
        await fixture.ResetAsync();
        var name = "confirmation-" + Guid.NewGuid().ToString("N");
        var actor = await fixture.CreateSyntheticUserAsync(name, Write, mfa);
        using var host = new IdentityHttpHost(fixture);
        var original = await Login(host, name, mfa);
        var authTime = Time(original);
        clock.Advance(TimeSpan.FromHours(20));
        var old = Assert.IsType<SessionIssued>(await host.RefreshAsync(original.Session.RefreshToken));
        Assert.Equal(authTime, Time(old));
        Assert.Equal(AuthenticationFailure.ReauthenticationRequired,
            Assert.IsType<AuthenticationRefused>(await host.AdminSetActiveAsync(old.Session.Token, fixture.UserID, false)).Code);
        var pkce = IdentityHttpHost.Pkce();
        var challenge = Assert.IsType<ChallengeRequired>(await Post(host, "admin-confirmation", old.Session.Token, new StartPasswordChangeRequest(pkce.Challenge)));
        var result = await Post(host, "admin-confirmation/password", old.Session.Token,
            new AdministratorPasswordProofRequest(challenge.Challenge.Handle!, fixture.Password, pkce.Verifier));
        if (mfa)
        {
            var pending = Assert.IsType<ChallengeRequired>(result);
            Assert.Equal(AuthenticationStep.ExistingMfa, pending.Challenge.Step);
            result = await Post(host, "admin-confirmation/mfa", old.Session.Token,
                new AdministratorMfaProofRequest(pending.Challenge.Handle!, (await fixture.GetSyntheticFactorAsync(name, clock.GetUtcNow())).Code!, pkce.Verifier));
        }
        var confirmed = Assert.IsType<SessionIssued>(result);
        Assert.Equal(actor.ToString(), confirmed.Session.UserData!.ID);
        Assert.True(long.Parse(Time(confirmed)) > long.Parse(authTime));
        var changed = Assert.IsType<AdminAccountChanged>(await host.AdminSetActiveAsync(confirmed.Session.Token, fixture.UserID, false));
        Assert.True(changed.Applied);
        Assert.IsType<AuthenticationRefused>(await Post(host, "admin-confirmation/password", old.Session.Token,
            new AdministratorPasswordProofRequest(challenge.Challenge.Handle!, fixture.Password, pkce.Verifier)));
        await using var db = fixture.CreateContext();
        Assert.Equal(1, await db.Set<AuthenticationAuditEvent>().CountAsync(x => x.UserID == actor && x.Outcome == "AdministratorConfirmed"));
        Assert.Equal(1, await db.Set<AuthenticationAuditEvent>().CountAsync(x => x.UserID == fixture.UserID && x.Outcome == "AccountDeactivated"));
        Assert.All(await db.Set<AuthenticationOperation>().Where(x => x.Purpose == AuthenticationOperationPurpose.AdministratorConfirmation).ToArrayAsync(),
            x => { Assert.Equal(AuthenticationOperationState.Completed, x.State); Assert.Null(x.SourceSessionDigest); });
        clock.Advance(TimeSpan.FromHours(19.9));
        var renewed = Assert.IsType<SessionIssued>(await host.RefreshAsync(confirmed.Session.RefreshToken));
        Assert.Equal(Time(confirmed), Time(renewed));
        Assert.IsType<AdminAccountChanged>(await host.AdminSetActiveAsync(renewed.Session.Token, fixture.UserID, true));
        clock.Advance(TimeSpan.FromMinutes(6));
        var expiredGrace = Assert.IsType<SessionIssued>(await host.RefreshAsync(renewed.Session.RefreshToken));
        Assert.Equal(AuthenticationFailure.ReauthenticationRequired,
            Assert.IsType<AuthenticationRefused>(await host.AdminSetActiveAsync(expiredGrace.Session.Token, fixture.UserID, false)).Code);
    }

    [Theory]
    [InlineData("wrong-password")]
    [InlineData("other-actor")]
    [InlineData("different-session")]
    [InlineData("cancel")]
    [InlineData("expired")]
    [InlineData("version")]
    [InlineData("policy")]
    [InlineData("factor")]
    [InlineData("inactive")]
    [InlineData("password-change")]
    [InlineData("recovery")]
    public async Task Invalid_or_changed_context_never_confirms_or_changes_the_target(string scenario)
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow); fixture.Clock = clock;
        await fixture.ResetAsync();
        var name = "confirmation-" + Guid.NewGuid().ToString("N");
        var actor = await fixture.CreateSyntheticUserAsync(name, Write);
        using var host = new IdentityHttpHost(fixture);
        var session = await Login(host, name);
        var access = session.Session.Token;
        var pkce = IdentityHttpHost.Pkce();
        var pending = Assert.IsType<ChallengeRequired>(await Post(host, "admin-confirmation", access, new StartPasswordChangeRequest(pkce.Challenge)));
        if (scenario == "other-actor") access = (await Login(host, fixture.Username)).Session.Token;
        if (scenario == "different-session") access = (Assert.IsType<SessionIssued>(await host.RefreshAsync(session.Session.RefreshToken))).Session.Token;
        if (scenario == "cancel") Assert.IsType<OperationCancelled>(await host.CancelAsync(pending.Challenge.Handle!, pkce.Verifier));
        if (scenario == "expired") clock.Advance(TimeSpan.FromMinutes(5));
        await using (var db = fixture.CreateContext())
        {
            if (scenario == "version") await db.Set<UserSecurityState>().Where(x => x.UserID == actor).ExecuteUpdateAsync(x => x.SetProperty(s => s.SecurityVersion, 2));
            if (scenario == "factor") await db.Set<UserSecurityState>().Where(x => x.UserID == actor).ExecuteUpdateAsync(x => x.SetProperty(s => s.FactorGeneration, 2));
            if (scenario == "policy") await db.Set<AuthenticationPolicyState>().ExecuteUpdateAsync(x => x.SetProperty(s => s.Revision, 2));
            if (scenario == "inactive") await db.Users.Where(x => x.ID == actor).ExecuteUpdateAsync(x => x.SetProperty(s => s.IsActive, false));
            if (scenario == "password-change") await db.Users.Where(x => x.ID == actor).ExecuteUpdateAsync(x => x.SetProperty(s => s.RequireChangePassword, true));
            if (scenario == "recovery") await db.Set<UserSecurityState>().Where(x => x.UserID == actor).ExecuteUpdateAsync(x => x.SetProperty(s => s.LocalMfaRecoveryRequired, true));
        }
        var result = await Post(host, "admin-confirmation/password", access,
            new AdministratorPasswordProofRequest(pending.Challenge.Handle!, scenario == "wrong-password" ? "Wrong password" : fixture.Password, pkce.Verifier));
        Assert.IsType<AuthenticationRefused>(result);
        await using var check = fixture.CreateContext();
        Assert.True((await check.Users.SingleAsync(x => x.ID == fixture.UserID)).IsActive);
        Assert.Equal(1, (await check.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID)).SecurityVersion);
        Assert.False(await check.Set<AuthenticationAuditEvent>().AnyAsync(x => x.Outcome == "AdministratorConfirmed"));
        if (scenario == "wrong-password") Assert.Equal(1, (await check.Set<UserSecurityState>().SingleAsync(x => x.UserID == actor)).FailedProofs);
    }

    [Fact]
    public async Task Permission_denial_is_distinct_from_old_proof_and_standalone_links_are_exempt()
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow); fixture.Clock = clock;
        await fixture.ResetAsync();
        var name = "confirmation-" + Guid.NewGuid().ToString("N");
        var actor = await fixture.CreateSyntheticUserAsync(name, Write);
        using var host = new IdentityHttpHost(fixture);
        var session = await Login(host, name);
        clock.Advance(TimeSpan.FromHours(21));
        session = Assert.IsType<SessionIssued>(await host.RefreshAsync(session.Session.RefreshToken));
        Assert.IsType<SecurityDeliveryRequested>(await Post(host, "email-verification/admin", session.Session.Token, new AdminEmailVerificationRequest(fixture.UserID)));
        Assert.IsType<SecurityDeliveryRequested>(await Post(host, "password-reset/admin", session.Session.Token, new AdminPasswordResetRequest(fixture.UserID)));
        await using var db = fixture.CreateContext();
        await db.Users.Where(x => x.ID == actor).ExecuteUpdateAsync(x => x.SetProperty(u => u.AccessTree, (string?)null));
        Assert.Equal(AuthenticationFailure.ClientDenied,
            Assert.IsType<AuthenticationRefused>(await host.AdminSetActiveAsync(session.Session.Token, fixture.UserID, false)).Code);
    }

    [Fact]
    public async Task Competing_password_completions_issue_only_one_confirmed_session()
    {
        fixture.Clock = new ControlledClock(DateTimeOffset.UtcNow); await fixture.ResetAsync();
        var name = "confirmation-" + Guid.NewGuid().ToString("N");
        await fixture.CreateSyntheticUserAsync(name, Write);
        using var first = new IdentityHttpHost(fixture); using var second = new IdentityHttpHost(fixture);
        var session = await Login(first, name); var pkce = IdentityHttpHost.Pkce();
        var pending = Assert.IsType<ChallengeRequired>(await Post(first, "admin-confirmation", session.Session.Token, new StartPasswordChangeRequest(pkce.Challenge)));
        var body = new AdministratorPasswordProofRequest(pending.Challenge.Handle!, fixture.Password, pkce.Verifier);
        var results = await Task.WhenAll(Post(first, "admin-confirmation/password", session.Session.Token, body), Post(second, "admin-confirmation/password", session.Session.Token, body));
        Assert.Single(results.OfType<SessionIssued>()); Assert.Single(results.OfType<AuthenticationRefused>());
    }

    private async Task<SessionIssued> Login(IdentityHttpHost host, string name, bool mfa = false)
    {
        var pkce = IdentityHttpHost.Pkce();
        var result = await IdentityHttpHost.Read(await host.Client.PostAsJsonAsync("/api/identity/v2/login", new PasswordLoginRequest(name, fixture.Password, pkce.Challenge)));
        if (mfa)
        {
            var pending = Assert.IsType<ChallengeRequired>(result);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/v2/login/mfa")
            { Content = JsonContent.Create(new CompleteMfaRequest((await fixture.GetSyntheticFactorAsync(name, fixture.Clock.GetUtcNow())).Code!, pkce.Verifier)) };
            request.Headers.Authorization = new("Operation", pending.Challenge.Handle);
            result = await IdentityHttpHost.Read(await host.Client.SendAsync(request));
        }
        return Assert.IsType<SessionIssued>(result);
    }

    private static string Time(SessionIssued session) => new JsonWebToken(session.Session.Token).GetClaim("auth_time").Value;
    private static async Task<AuthOutcome> Post<T>(IdentityHttpHost host, string route, string access, T body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/v2/" + route) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new("Bearer", access);
        return await IdentityHttpHost.Read(await host.Client.SendAsync(request));
    }
}
