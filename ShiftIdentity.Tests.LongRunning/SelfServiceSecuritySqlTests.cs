using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OtpNet;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using Xunit;
using static ShiftIdentity.Tests.LegacyLoginCompatibilitySqlTests;

namespace ShiftIdentity.Tests;

/// <summary>
/// Self-service security on the staged authority, the way the identity host's own dashboard runs it after cutover:
/// a session from the deployed login route changes its password, sets up and replaces its authenticator through the
/// staged flows, an administrator resets an authenticator through the deployed bulk route, and the deployed profile
/// route keeps its request and response shapes. Real dashboard registration with the staged authority on the same
/// scoped context, an owned fixture database and synthetic accounts only. The class owns its database: it adds an
/// operator account, and the shared "Identity SQL" database is assumed by its classes to hold exactly one user.
/// </summary>
[Trait("Category", "Sql")]
[Trait("Category", "Http")]
public sealed class SelfServiceSecuritySqlTests(SqlIdentityFixture fixture) : IClassFixture<SqlIdentityFixture>
{
    private const string NewPassword = "Self chosen synthetic phrase 51!";
    private const string AdminName = "synthetic-self-service-admin";
    // Users read/write/delete and the dedicated recovery permission, plus wildcard data-level access on every
    // dimension the User row carries (the bulk routes select their targets through the data-level filter).
    private const string AdminTree = "{\"ShiftIdentityActions\":{\"Users\":[\"r\",\"w\",\"d\"],\"ManageMfaRecovery\":[\"m\"],\"AccessTrees\":[\"r\"],\"DataLevelAccess\":{\"Countries\":[\"r\",\"w\",\"d\"],\"Regions\":[\"r\",\"w\",\"d\"],\"Companies\":[\"r\",\"w\",\"d\"],\"Branches\":[\"r\",\"w\",\"d\"]}}}";
    private const string ReadOnlyTree = "{\"ShiftIdentityActions\":{\"Users\":[\"r\"],\"DataLevelAccess\":{\"Countries\":[\"r\"],\"Regions\":[\"r\"],\"Companies\":[\"r\"],\"Branches\":[\"r\"]}}}";
    private const string Email = "self-service@example.invalid";
    private const string Phone = "+12025550123";
    private ControlledClock clock = null!;

    // ── the dashboard's password change ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Voluntary_password_change_from_a_deployed_login_session_uses_the_staged_flow(bool mfa)
    {
        await PrepareAsync(mfa);
        using var host = Host();
        var session = await SessionAsync(host, mfa);
        var before = await State();
        var pkce = IdentityHttpHost.Pkce();
        var start = Challenge(await Staged(host, HttpMethod.Post, "password-change", "Bearer", session.Token, new StartPasswordChangeRequest(pkce.Challenge)), AuthenticationStep.Password);
        // The current password is proven afresh; the session's own claims establish context only.
        Assert.IsType<AuthenticationRefused>(await Staged(host, HttpMethod.Post, "password-change/password", "Operation", start.Handle!, new PasswordChangeProofRequest("wrong password", pkce.Verifier)));
        var proven = await Staged(host, HttpMethod.Post, "password-change/password", "Operation", start.Handle!, new PasswordChangeProofRequest(fixture.Password, pkce.Verifier));
        AuthenticationChallenge ready;
        if (mfa)
        {
            var factor = Challenge(proven, AuthenticationStep.ExistingMfa);
            clock.Advance(TimeSpan.FromSeconds(30));
            ready = Challenge(await Staged(host, HttpMethod.Post, "password-change/mfa", "Operation", factor.Handle!, new CompleteMfaRequest(ActiveCode(), pkce.Verifier)), AuthenticationStep.PasswordChange);
        }
        else ready = Challenge(proven, AuthenticationStep.PasswordChange);
        Assert.Equal(AuthenticationFailure.InvalidNewPassword, Assert.IsType<AuthenticationRefused>(await Staged(host, HttpMethod.Post, "password-change/complete", "Operation", ready.Handle!,
            new CompletePasswordChangeRequest("short", pkce.Verifier))).Code);
        var changed = Assert.IsType<PasswordChanged>(await Staged(host, HttpMethod.Post, "password-change/complete", "Operation", ready.Handle!, new CompletePasswordChangeRequest(NewPassword, pkce.Verifier)));
        var fresh = Assert.IsType<SessionIssued>(changed.Continuation).Session;
        Assert.Equal(AuthPurpose.None, fresh.Flow);
        Assert.Equal("2", Jwt(fresh.Token)["shift_sv"]);
        Assert.Equal(mfa ? "true" : "false", Jwt(fresh.Token)["shift_mfa"]);
        // Only the initiating device holds the fresh credentials; the previous session ends at its next renewal.
        await Entity<TokenDTO>(await RefreshDeployed(host, fresh.RefreshToken));
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshDeployed(host, session.RefreshToken)).StatusCode);
        var user = await UserRow(); var state = await State();
        Assert.True(HashService.VerifyVersionedPassword(NewPassword, user.Salt, user.PasswordHash));
        Assert.False(user.RequireChangePassword);
        Assert.Equal(2, state.SecurityVersion);
        Assert.Equal(before.FactorGeneration, state.FactorGeneration);
        Assert.Equal(before.ProtectedTotpSecret, state.ProtectedTotpSecret);
        Assert.Single(await Audits("PasswordChanged"));
        // The deployed login accepts the new password and refuses the old one.
        Assert.Equal(HttpStatusCode.BadRequest, (await Login(host)).StatusCode);
        var again = await Entity<TokenDTO>(await Login(host, NewPassword));
        Assert.Equal(mfa ? AuthPurpose.Mfa : AuthPurpose.None, again.Flow);
    }

    [Fact]
    public async Task Cancelling_a_voluntary_change_keeps_the_password_and_the_session()
    {
        await PrepareAsync();
        using var host = Host();
        var session = await SessionAsync(host);
        var pkce = IdentityHttpHost.Pkce();
        var start = Challenge(await Staged(host, HttpMethod.Post, "password-change", "Bearer", session.Token, new StartPasswordChangeRequest(pkce.Challenge)), AuthenticationStep.Password);
        var ready = Challenge(await Staged(host, HttpMethod.Post, "password-change/password", "Operation", start.Handle!, new PasswordChangeProofRequest(fixture.Password, pkce.Verifier)), AuthenticationStep.PasswordChange);
        Assert.IsType<OperationCancelled>(await Staged(host, HttpMethod.Post, "operations/cancel", "Operation", ready.Handle!, new CancelOperationRequest(pkce.Verifier)));
        Assert.IsType<AuthenticationRefused>(await Staged(host, HttpMethod.Post, "password-change/complete", "Operation", ready.Handle!, new CompletePasswordChangeRequest(NewPassword, pkce.Verifier)));
        var user = await UserRow();
        Assert.True(HashService.VerifyVersionedPassword(fixture.Password, user.Salt, user.PasswordHash));
        Assert.Equal(1, (await State()).SecurityVersion);
        await Entity<TokenDTO>(await RefreshDeployed(host, session.RefreshToken));
    }

    // ── the dashboard's authenticator set-up and replacement ────────────────────────────────────────────────────

    [Fact]
    public async Task First_enrollment_then_replacement_from_a_deployed_login_session()
    {
        await PrepareAsync();
        using var host = Host();
        var session = await SessionAsync(host);
        var status = Assert.IsType<AuthenticatorStatus>(await Staged(host, HttpMethod.Get, "mfa", "Bearer", session.Token));
        Assert.False(status.Enrolled); Assert.False(status.RecoveryRequired);

        // First enrollment: the account password, then a code from the new authenticator. Asking for a replacement
        // while nothing is enrolled is refused; the client's intent must match the authoritative state.
        Assert.Equal(AuthenticationFailure.InvalidGrant, Assert.IsType<AuthenticationRefused>(await Staged(host, HttpMethod.Post, "mfa/start", "Bearer", session.Token,
            new StartMfaRequest(IdentityHttpHost.Pkce().Challenge, Replace: true))).Code);
        var pkce = IdentityHttpHost.Pkce();
        var start = Challenge(await Staged(host, HttpMethod.Post, "mfa/start", "Bearer", session.Token, new StartMfaRequest(pkce.Challenge)), AuthenticationStep.Password);
        Assert.IsType<AuthenticationRefused>(await Staged(host, HttpMethod.Post, "mfa/password", "Operation", start.Handle!, new PasswordChangeProofRequest("wrong password", pkce.Verifier)));
        var setup = Challenge(await Staged(host, HttpMethod.Post, "mfa/password", "Operation", start.Handle!, new PasswordChangeProofRequest(fixture.Password, pkce.Verifier)), AuthenticationStep.NewMfa);
        Assert.Null((await State()).ProtectedTotpSecret);
        Assert.IsType<AuthenticationRefused>(await Staged(host, HttpMethod.Post, "mfa/confirm", "Operation", setup.Handle!, new CompleteMfaRequest("000000", pkce.Verifier)));
        var enrolledResult = Assert.IsType<MfaChanged>(await Staged(host, HttpMethod.Post, "mfa/confirm", "Operation", setup.Handle!, new CompleteMfaRequest(Code(setup), pkce.Verifier)));
        var enrolled = Assert.IsType<SessionIssued>(enrolledResult.Continuation).Session;
        Assert.Equal("true", Jwt(enrolled.Token)["shift_mfa"]);
        Assert.True(Assert.IsType<AuthenticatorStatus>(await Staged(host, HttpMethod.Get, "mfa", "Bearer", enrolled.Token)).Enrolled);
        Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await Staged(host, HttpMethod.Get, "mfa", "Bearer", session.Token)).Code);
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshDeployed(host, session.RefreshToken)).StatusCode);
        await Entity<TokenDTO>(await RefreshDeployed(host, enrolled.RefreshToken));
        var state = await State();
        Assert.Equal(2, state.SecurityVersion); Assert.Equal(2, state.FactorGeneration);
        Assert.Equal(Base32Encoding.ToBytes(setup.NewAuthenticator!.Secret), fixture.ReadSyntheticFactor(state));
        // The retained plaintext column is never written by the staged flows.
        Assert.Null((await UserRow()).TotpSecret);

        // Replacement: proof of the existing authenticator, never the password; the old factor stays active until
        // the new one is confirmed.
        clock.Advance(TimeSpan.FromSeconds(30));
        var second = IdentityHttpHost.Pkce();
        var replace = Challenge(await Staged(host, HttpMethod.Post, "mfa/start", "Bearer", enrolled.Token, new StartMfaRequest(second.Challenge, Replace: true)), AuthenticationStep.ExistingMfa);
        Assert.IsType<AuthenticationRefused>(await Staged(host, HttpMethod.Post, "mfa/password", "Operation", replace.Handle!, new PasswordChangeProofRequest(fixture.Password, second.Verifier)));
        Assert.IsType<AuthenticationRefused>(await Staged(host, HttpMethod.Post, "mfa/existing", "Operation", replace.Handle!, new CompleteMfaRequest("000000", second.Verifier)));
        var replacement = Challenge(await Staged(host, HttpMethod.Post, "mfa/existing", "Operation", replace.Handle!, new CompleteMfaRequest(Code(setup), second.Verifier)), AuthenticationStep.NewMfa);
        Assert.Equal(Base32Encoding.ToBytes(setup.NewAuthenticator.Secret), fixture.ReadSyntheticFactor(await State()));
        var replacedResult = Assert.IsType<MfaChanged>(await Staged(host, HttpMethod.Post, "mfa/confirm", "Operation", replacement.Handle!, new CompleteMfaRequest(Code(replacement), second.Verifier)));
        var replaced = Assert.IsType<SessionIssued>(replacedResult.Continuation).Session;
        state = await State();
        Assert.Equal(3, state.SecurityVersion); Assert.Equal(3, state.FactorGeneration);
        Assert.Equal(Base32Encoding.ToBytes(replacement.NewAuthenticator!.Secret), fixture.ReadSyntheticFactor(state));
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshDeployed(host, enrolled.RefreshToken)).StatusCode);
        await Entity<TokenDTO>(await RefreshDeployed(host, replaced.RefreshToken));
        Assert.Equal(new[] { "MfaEnrolled", "MfaReplaced" }, (await Audits("MfaEnrolled", "MfaReplaced")).Select(x => x.Outcome));

        // The deployed login now needs the replacement authenticator; the old one is gone.
        clock.Advance(TimeSpan.FromSeconds(30));
        var step = await Entity<TokenDTO>(await Login(host));
        Assert.Equal(AuthPurpose.Mfa, step.Flow);
        Assert.Equal(HttpStatusCode.BadRequest, (await Mfa(host, step.Token, Code(setup))).StatusCode);
        await Entity<TokenDTO>(await Mfa(host, step.Token, Code(replacement)));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("step")]
    [InlineData("refresh")]
    [InlineData("stale")]
    [InlineData("recovery")]
    public async Task Authenticator_status_requires_a_current_ordinary_session(string scenario)
    {
        await PrepareAsync(forced: scenario == "step");
        using var host = Host();
        var login = await Entity<TokenDTO>(await Login(host));
        Assert.Equal(scenario == "step" ? AuthPurpose.ChangePassword : AuthPurpose.None, login.Flow);
        var audits = await AuditCountAsync();
        if (scenario == "stale")
            await using (var db = fixture.CreateContext())
                await db.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(s => s.SecurityVersion, s => s.SecurityVersion + 1));
        if (scenario == "recovery")
            await using (var db = fixture.CreateContext())
                await db.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(s => s.LocalMfaRecoveryRequired, true));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/identity/v2/mfa");
        if (scenario != "missing")
            request.Headers.Authorization = new("Bearer", scenario == "refresh" ? login.RefreshToken : login.Token);
        using var response = await host.Client.SendAsync(request);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var outcome = (await response.Content.ReadFromJsonAsync<AuthOutcome>())!;
        switch (scenario)
        {
            case "recovery":
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var status = Assert.IsType<AuthenticatorStatus>(outcome);
                Assert.False(status.Enrolled); Assert.True(status.RecoveryRequired);
                break;
            case "stale":
                // A stale version is the same conflict every staged route reports.
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(outcome).Code);
                break;
            default:
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal(AuthenticationFailure.InvalidGrant, Assert.IsType<AuthenticationRefused>(outcome).Code);
                break;
        }
        // A read: no audit row, no operation, no state change.
        Assert.Equal(audits, await AuditCountAsync());
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Set<AuthenticationOperation>().AnyAsync(x => x.UserID == fixture.UserID && x.Purpose != AuthenticationOperationPurpose.PasswordChange));
    }

    [Fact]
    public async Task Deployed_voluntary_routes_refuse_an_ordinary_session_under_the_staged_authority()
    {
        await PrepareAsync();
        using var host = Host();
        var session = await SessionAsync(host);
        var audits = await AuditCountAsync();
        // The dashboard moved to the staged flows for these; the deployed routes serve only the forced steps of a login.
        using (var change = await Send(host.Client, HttpMethod.Put, "/api/UserManager/ChangePassword", session.Token,
            new ChangePasswordDTO { CurrentPassword = fixture.Password, NewPassword = NewPassword, ConfirmPassword = NewPassword }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
            Assert.Null((await change.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!.Entity);
        }
        using (var start = await Send(host.Client, HttpMethod.Get, "/api/UserManager/StartTotpEnrollment", session.Token))
        {
            Assert.Equal(HttpStatusCode.BadRequest, start.StatusCode);
            Assert.Null((await start.Content.ReadFromJsonAsync<ShiftEntityResponse<TotpDTO>>())!.Entity);
        }
        using (var confirm = await Send(host.Client, HttpMethod.Post, "/api/UserManager/ConfirmTotpEnrollment", session.Token,
            new TotpDTO { Secret = "JBSWY3DPEHPK3PXP", SasToken = "tampered", Expires = "0", Code = "123456" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
            Assert.Null((await confirm.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!.Entity);
        }
        var user = await UserRow(); var state = await State();
        Assert.True(HashService.VerifyVersionedPassword(fixture.Password, user.Salt, user.PasswordHash));
        Assert.Null(user.TotpSecret); Assert.Null(state.ProtectedTotpSecret);
        Assert.Equal(1, state.SecurityVersion); Assert.Equal(1, state.FactorGeneration);
        Assert.Equal(audits, await AuditCountAsync());
        await Entity<TokenDTO>(await RefreshDeployed(host, session.RefreshToken));
    }

    // ── the deployed profile route keeps its shapes ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Profile_save_changes_only_ordinary_fields_and_refuses_identifier_and_contact_changes()
    {
        await PrepareAsync();
        await using (var db = fixture.CreateContext())
        {
            var saved = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
            var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
            saved.Email = Email; saved.Phone = Phone; saved.EmailVerified = true; saved.PhoneVerified = true;
            state.EmailLookupKey = RecoveryContact.Key(Email);
            await db.SaveChangesAsync();
        }
        using var host = Host();
        var session = await SessionAsync(host);
        var profile = await Entity<UserDataDTO>(await Send(host.Client, HttpMethod.Get, "/api/UserManager/UserData", session.Token));
        Assert.Equal(fixture.Username, profile.Username); Assert.Equal(Email, profile.Email); Assert.Equal(Phone, profile.Phone);
        Assert.Equal("Synthetic User", profile.FullName); Assert.True(profile.EmailVerified); Assert.True(profile.PhoneVerified);

        // Full name, birth date and signature save without further authentication and without a version change.
        profile.FullName = "Synthetic User (edited)"; profile.BirthDate = new DateTime(1990, 1, 2);
        var edited = await Entity<UserDataDTO>(await Send(host.Client, HttpMethod.Put, "/api/UserManager/UserData", session.Token, profile));
        Assert.Equal("Synthetic User (edited)", edited.FullName);
        var user = await UserRow();
        Assert.Equal("Synthetic User (edited)", user.FullName); Assert.Equal(new DateTime(1990, 1, 2), user.BirthDate);
        Assert.Equal(1, (await State()).SecurityVersion);
        await Entity<TokenDTO>(await RefreshDeployed(host, session.RefreshToken));

        // A changed username, email or phone is refused before any write, with the existing messages.
        foreach (var (field, apply) in new (string, Action<UserDataDTO>)[]
        {
            ("Username", dto => dto.Username = "someone-else"),
            ("ContactReauthenticationRequired", dto => dto.Email = "other@example.invalid"),
            ("ContactReauthenticationRequired", dto => dto.Email = null),
            ("ContactReauthenticationRequired", dto => dto.Phone = "+12025550199"),
        })
        {
            var attempt = await Entity<UserDataDTO>(await Send(host.Client, HttpMethod.Get, "/api/UserManager/UserData", session.Token));
            attempt.FullName = "Synthetic User (rewritten)"; apply(attempt);
            using var refused = await Send(host.Client, HttpMethod.Put, "/api/UserManager/UserData", session.Token, attempt);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            var message = (await refused.Content.ReadFromJsonAsync<ShiftEntityResponse<UserDataDTO>>())!.Message!;
            Assert.Equal(field, message.For);
            var after = await UserRow();
            Assert.Equal("Synthetic User (edited)", after.FullName); Assert.Equal(fixture.Username, after.Username);
            Assert.Equal(Email, after.Email); Assert.Equal(Phone, after.Phone); Assert.True(after.EmailVerified);
        }
        // Equivalent spellings of the unchanged fields are accepted without rewriting them.
        var same = await Entity<UserDataDTO>(await Send(host.Client, HttpMethod.Get, "/api/UserManager/UserData", session.Token));
        same.Username = " " + fixture.Username.ToUpperInvariant() + " "; same.Email = Email.ToUpperInvariant(); same.FullName = "Synthetic User (again)";
        await Entity<UserDataDTO>(await Send(host.Client, HttpMethod.Put, "/api/UserManager/UserData", session.Token, same));
        var final = await UserRow();
        Assert.Equal("Synthetic User (again)", final.FullName); Assert.Equal(fixture.Username, final.Username); Assert.Equal(Email, final.Email);
        Assert.Equal(1, (await State()).SecurityVersion);
    }

    // ── the deployed bulk ResetTotp route on the staged boundary ────────────────────────────────────────────────

    [Fact]
    public async Task Bulk_reset_through_the_deployed_route_clears_the_staged_factor_and_requires_recovery()
    {
        await PrepareAsync(mfa: true);
        // A migrated legacy row still carries the plaintext copy; the staged reset must not touch it.
        await SetPlaintextFactorAsync(fixture.FactorSecret);
        var adminID = await AdminAsync();
        using var host = Host();
        var target = await SessionAsync(host, mfa: true);
        var admin = await AdminSessionAsync(host);

        using (var response = await ResetTotp(host, admin.Token, fixture.UserID))
        {
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var envelope = (await response.Content.ReadFromJsonAsync<ShiftEntityResponse<IEnumerable<UserInfoDTO>>>())!;
            var info = Assert.Single(envelope.Entity!);
            Assert.Equal(fixture.Username, info.Username); Assert.False(info.TotpEnabled);
        }
        var state = await State();
        Assert.Null(state.ProtectedTotpSecret); Assert.Equal(0, state.TotpProtectionVersion); Assert.Null(state.LastAcceptedTotpStep);
        Assert.True(state.LocalMfaRecoveryRequired); Assert.Null(state.MfaRecoveryOperationID);
        Assert.Equal(2, state.FactorGeneration); Assert.Equal(2, state.SecurityVersion);
        Assert.Equal(fixture.FactorSecret, (await UserRow()).TotpSecret);
        var audit = Assert.Single(await Audits("MfaReset"));
        Assert.Equal(adminID, audit.ActorUserID); Assert.Equal(2, audit.SecurityVersion);

        // The account's sessions end at renewal and a password alone opens no ordinary session, on either contract.
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshDeployed(host, target.RefreshToken)).StatusCode);
        using (var login = await Login(host))
        {
            Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
            var body = (await login.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!;
            Assert.Null(body.Entity); Assert.Contains("recovery", body.Message!.Body, StringComparison.OrdinalIgnoreCase);
        }
        var pkce = IdentityHttpHost.Pkce();
        Assert.Equal(AuthenticationStep.MfaRecovery, Assert.IsType<ChallengeRequired>(await Staged(host, HttpMethod.Post, "login", null, null,
            new PasswordLoginRequest(fixture.Username, fixture.Password, pkce.Challenge))).Challenge.Step);
        Assert.False(await Staged(host, HttpMethod.Post, "login", null, null, new PasswordLoginRequest(fixture.Username, fixture.Password, pkce.Challenge)) is SessionIssued);

        // A second reset changes nothing more.
        using (var again = await ResetTotp(host, admin.Token, fixture.UserID))
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(2, (await State()).SecurityVersion);
        Assert.Single(await Audits("MfaReset"));

        // Individual recovery remains the way back: the dedicated permission issues a code, the user proves the
        // password and the code, confirms a new authenticator and signs in again with both.
        var issued = Assert.IsType<MfaRecoveryCodeIssued>(await Staged(host, HttpMethod.Post, "mfa/recovery-code", "Bearer", admin.Token,
            new IssueMfaRecoveryRequest(fixture.UserID, "Synthetic check after bulk reset")));
        Assert.Equal(2, (await State()).SecurityVersion);
        var recovery = IdentityHttpHost.Pkce();
        Assert.IsType<AuthenticationRefused>(await Staged(host, HttpMethod.Post, "mfa/recover", null, null, new RecoverMfaRequest(fixture.Username, "wrong password", issued.Code, recovery.Challenge)));
        var setup = Challenge(await Staged(host, HttpMethod.Post, "mfa/recover", null, null, new RecoverMfaRequest(fixture.Username, fixture.Password, issued.Code, recovery.Challenge)), AuthenticationStep.NewMfa);
        var recovered = Assert.IsType<MfaChanged>(await Staged(host, HttpMethod.Post, "mfa/confirm", "Operation", setup.Handle!, new CompleteMfaRequest(Code(setup), recovery.Verifier)));
        Assert.IsType<ReturnToLogin>(recovered.Continuation);
        state = await State();
        Assert.False(state.LocalMfaRecoveryRequired); Assert.Equal(3, state.SecurityVersion); Assert.Equal(3, state.FactorGeneration);
        clock.Advance(TimeSpan.FromSeconds(30));
        var step = await Entity<TokenDTO>(await Login(host));
        Assert.Equal(AuthPurpose.Mfa, step.Flow);
        Assert.Equal(HttpStatusCode.BadRequest, (await Mfa(host, step.Token, ActiveCode())).StatusCode);
        await Entity<TokenDTO>(await Mfa(host, step.Token, Code(setup)));
    }

    [Theory]
    [InlineData("self")]
    [InlineData("stale-proof")]
    [InlineData("permission")]
    [InlineData("protected")]
    [InlineData("no-factor")]
    public async Task Bulk_reset_refusals_and_no_ops_change_nothing(string scenario)
    {
        await PrepareAsync(mfa: scenario != "no-factor");
        var adminID = await AdminAsync(scenario == "permission" ? ReadOnlyTree : AdminTree);
        using var host = Host();
        var admin = await AdminSessionAsync(host);
        var targetID = scenario == "self" ? adminID : fixture.UserID;
        var before = await State(targetID);
        try
        {
            if (scenario == "protected")
                await using (var db = fixture.CreateContext())
                    await db.Users.Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsProtected, true));
            if (scenario == "stale-proof") clock.Advance(TimeSpan.FromMinutes(6));
            using var response = await ResetTotp(host, admin.Token, targetID);
            switch (scenario)
            {
                case "self":
                case "stale-proof":
                case "permission":
                    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                    break;
                default:
                    Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
                    break;
            }
            var state = await State(targetID);
            Assert.Equal(before.SecurityVersion, state.SecurityVersion); Assert.Equal(before.FactorGeneration, state.FactorGeneration);
            Assert.Equal(before.ProtectedTotpSecret, state.ProtectedTotpSecret); Assert.Equal(before.LocalMfaRecoveryRequired, state.LocalMfaRecoveryRequired);
            await using var verify = fixture.CreateContext();
            Assert.False(await verify.Set<AuthenticationAuditEvent>().AnyAsync(x => x.UserID == targetID && x.Outcome == "MfaReset"));
        }
        finally
        {
            await using var restore = fixture.CreateContext();
            await restore.Users.Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsProtected, false));
        }
    }

    [Fact]
    public async Task Bulk_reset_without_the_staged_authority_keeps_the_legacy_write()
    {
        await PrepareAsync(mfa: true);
        await SetPlaintextFactorAsync(fixture.FactorSecret);
        await AdminAsync();
        // Production registration: no staged services, the deployed issuer and the direct column write.
        using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture);
        var login = await Entity<TokenDTO>(await host.Client.PostAsJsonAsync("/api/Auth/Login", new LoginDTO { Username = AdminName, Password = fixture.Password }));
        Assert.Equal(AuthPurpose.None, login.Flow);
        using var response = await ResetTotp(host, login.Token, fixture.UserID);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.False(Assert.Single((await response.Content.ReadFromJsonAsync<ShiftEntityResponse<IEnumerable<UserInfoDTO>>>())!.Entity!).TotpEnabled);
        Assert.Null((await UserRow()).TotpSecret);
        // The staged state is not this host's authority and is left alone.
        var state = await State();
        Assert.NotNull(state.ProtectedTotpSecret); Assert.False(state.LocalMfaRecoveryRequired);
        Assert.Equal(1, state.SecurityVersion); Assert.Equal(1, state.FactorGeneration);
        Assert.Empty(await Audits("MfaReset"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Bulk_reset_and_a_pending_deployed_MFA_login_serialize_in_both_lock_orders(bool resetFirst)
    {
        await PrepareAsync(mfa: true);
        await AdminAsync();
        using var setup = Host();
        var admin = await AdminSessionAsync(setup);
        var step = await Entity<TokenDTO>(await Login(setup));
        Assert.Equal(AuthPurpose.Mfa, step.Flow);
        using var gate = new AdmissionGate("AdmissionLock");
        var signal = new SqlCommandSignal("UPDLOCK");
        using var resetHost = Host(resetFirst ? gate.Observe : null, resetFirst ? [] : [signal]);
        using var mfaHost = Host(resetFirst ? null : gate.Observe, resetFirst ? [signal] : []);
        Task<HttpResponseMessage>? reset = null, mfa = null;
        var code = ActiveCode();
        if (resetFirst) reset = ResetTotp(resetHost, admin.Token, fixture.UserID);
        else mfa = Mfa(mfaHost, step.Token, code);
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            if (resetFirst) mfa = Mfa(mfaHost, step.Token, code);
            else reset = ResetTotp(resetHost, admin.Token, fixture.UserID);
            await signal.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(resetFirst ? mfa!.IsCompleted : reset!.IsCompleted);
        }
        finally { gate.Release(); }
        using var resetResponse = await reset!;
        Assert.True(resetResponse.StatusCode == HttpStatusCode.OK, await resetResponse.Content.ReadAsStringAsync());
        using var mfaResponse = await mfa!;
        var state = await State();
        Assert.Null(state.ProtectedTotpSecret); Assert.True(state.LocalMfaRecoveryRequired); Assert.Equal(2, state.SecurityVersion);
        if (resetFirst)
        {
            // The pending login's factor generation is gone: no session from the removed authenticator.
            Assert.Equal(HttpStatusCode.BadRequest, mfaResponse.StatusCode);
            Assert.Empty(await Audits("SessionIssued"));
        }
        else
        {
            // The login admitted first issues its session at the old version; the reset then ends it at renewal.
            var session = await Entity<TokenDTO>(mfaResponse);
            Assert.Equal("1", Jwt(session.Token)["shift_sv"]);
            Assert.Equal(HttpStatusCode.BadRequest, (await RefreshDeployed(setup, session.RefreshToken)).StatusCode);
        }
    }

    [Fact]
    public async Task A_save_failure_after_the_admitted_reset_leaves_the_authenticator_active()
    {
        await PrepareAsync(mfa: true);
        await AdminAsync();
        using var healthy = Host();
        var admin = await AdminSessionAsync(healthy);
        using var faulty = Host(null, new SecurityStateSaveFault());
        HttpResponseMessage? response = null;
        try { response = await ResetTotp(faulty, admin.Token, fixture.UserID); }
        catch (Exception error) when (error is not Xunit.Sdk.XunitException) { }
        Assert.True(response is null || response.StatusCode == HttpStatusCode.InternalServerError);
        response?.Dispose();
        var state = await State();
        Assert.NotNull(state.ProtectedTotpSecret); Assert.False(state.LocalMfaRecoveryRequired);
        Assert.Equal(1, state.SecurityVersion); Assert.Equal(1, state.FactorGeneration);
        Assert.Empty(await Audits("MfaReset"));
        // The row is fully usable afterwards.
        using var retry = await ResetTotp(healthy, admin.Token, fixture.UserID);
        Assert.True(retry.StatusCode == HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        Assert.True((await State()).LocalMfaRecoveryRequired);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task PrepareAsync(bool mfa = false, bool forced = false)
    {
        clock = new ControlledClock(DateTimeOffset.UtcNow); fixture.Clock = clock;
        await fixture.ResetAsync(mfa);
        await using var db = fixture.CreateContext();
        var user = await db.Users.Include(x => x.UserLog).SingleAsync(x => x.ID == fixture.UserID);
        user.RequireChangePassword = forced; user.LoginAttempts = 0; user.LockDownUntil = null;
        user.FullName = "Synthetic User"; user.BirthDate = null; user.IsProtected = false; user.Phone = null; user.PhoneVerified = false;
        user.UserLog ??= new(); user.UserLog.LastSeen = DateTimeOffset.UnixEpoch;
        await db.SaveChangesAsync();
    }

    private async Task SetPlaintextFactorAsync(byte[] secret)
    {
        await using var db = fixture.CreateContext();
        await db.Users.Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(u => u.TotpSecret, secret));
    }

    private async Task<long> AdminAsync(string tree = AdminTree)
    {
        await using var db = fixture.CreateContext();
        var id = await db.Users.IgnoreQueryFilters().Where(x => x.Username == AdminName).Select(x => x.ID).SingleOrDefaultAsync();
        if (id == 0) id = await fixture.CreateSyntheticUserAsync(AdminName, tree);
        await db.Users.IgnoreQueryFilters().Where(x => x.ID == id).ExecuteUpdateAsync(x => x
            .SetProperty(u => u.IsActive, true).SetProperty(u => u.IsDeleted, false).SetProperty(u => u.IsProtected, false).SetProperty(u => u.AccessTree, tree));
        await db.Set<UserSecurityState>().Where(x => x.UserID == id).ExecuteUpdateAsync(x => x
            .SetProperty(s => s.SecurityVersion, 1).SetProperty(s => s.FailedProofs, 0).SetProperty(s => s.FailureWindowStart, (DateTimeOffset?)null)
            .SetProperty(s => s.LocalMfaRecoveryRequired, false));
        return id;
    }

    private LegacyIdentityHttpHost<IdentityTestDbContext> Host(Action<string>? observe = null, params IInterceptor[] interceptors) =>
        new(fixture, authority: true, observe, interceptors);

    private Task<HttpResponseMessage> Login(LegacyIdentityHttpHost<IdentityTestDbContext> host, string? password = null) =>
        host.Client.PostAsJsonAsync("/api/Auth/Login", new LoginDTO { Username = fixture.Username, Password = password ?? fixture.Password });

    private static Task<HttpResponseMessage> Mfa(LegacyIdentityHttpHost<IdentityTestDbContext> host, string token, string code) =>
        Send(host.Client, HttpMethod.Post, "/api/Auth/Login/mfa", token, new { Code = code });

    private static Task<HttpResponseMessage> RefreshDeployed(LegacyIdentityHttpHost<IdentityTestDbContext> host, string refreshToken) =>
        host.Client.PostAsJsonAsync("/api/Auth/Refresh", new { RefreshToken = refreshToken });

    private async Task<TokenDTO> SessionAsync(LegacyIdentityHttpHost<IdentityTestDbContext> host, bool mfa = false)
    {
        var token = await Entity<TokenDTO>(await Login(host));
        if (!mfa) { Assert.Equal(AuthPurpose.None, token.Flow); return token; }
        Assert.Equal(AuthPurpose.Mfa, token.Flow);
        return await Entity<TokenDTO>(await Mfa(host, token.Token, ActiveCode()));
    }

    private async Task<TokenDTO> AdminSessionAsync(LegacyIdentityHttpHost<IdentityTestDbContext> host) =>
        Assert.IsType<SessionIssued>(await Staged(host, HttpMethod.Post, "login", null, null,
            new PasswordLoginRequest(AdminName, fixture.Password, IdentityHttpHost.Pkce().Challenge))).Session;

    private static async Task<AuthOutcome> Staged(LegacyIdentityHttpHost<IdentityTestDbContext> host, HttpMethod method, string route,
        string? scheme, string? credential, object? body = null)
    {
        using var request = new HttpRequestMessage(method, "/api/identity/v2/" + route);
        if (scheme is not null) request.Headers.Authorization = new(scheme, credential);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await IdentityHttpHost.Read(await host.Client.SendAsync(request));
    }

    private static Task<HttpResponseMessage> ResetTotp(LegacyIdentityHttpHost<IdentityTestDbContext> host, string token, params long[] ids) =>
        Send(host.Client, HttpMethod.Post, "/api/IdentityUser/ResetTotp", token,
            new SelectStateDTO<UserListDTO> { Items = ids.Select(id => new UserListDTO { ID = id.ToString() }).ToList() });

    private static AuthenticationChallenge Challenge(AuthOutcome outcome, AuthenticationStep step)
    {
        var challenge = Assert.IsType<ChallengeRequired>(outcome).Challenge;
        Assert.Equal(step, challenge.Step);
        if (step == AuthenticationStep.NewMfa) Assert.NotNull(challenge.NewAuthenticator);
        return challenge;
    }

    private string ActiveCode() => new Totp(fixture.FactorSecret).ComputeTotp(clock.GetUtcNow().UtcDateTime);
    private string Code(AuthenticationChallenge setup) => new Totp(Base32Encoding.ToBytes(setup.NewAuthenticator!.Secret)).ComputeTotp(clock.GetUtcNow().UtcDateTime);

    private async Task<UserSecurityState> State(long? id = null)
    {
        await using var db = fixture.CreateContext();
        return await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == (id ?? fixture.UserID));
    }

    private async Task<User> UserRow()
    {
        await using var db = fixture.CreateContext();
        return await db.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.ID == fixture.UserID);
    }

    private async Task<List<AuthenticationAuditEvent>> Audits(params string[] outcomes)
    {
        await using var db = fixture.CreateContext();
        return await db.Set<AuthenticationAuditEvent>().AsNoTracking().Where(x => x.UserID == fixture.UserID && outcomes.Contains(x.Outcome))
            .OrderBy(x => x.CreatedAt).ToListAsync();
    }

    private async Task<int> AuditCountAsync()
    {
        await using var db = fixture.CreateContext();
        return await db.Set<AuthenticationAuditEvent>().CountAsync(x => x.UserID == fixture.UserID);
    }

    /// <summary>Fails the flush of a save whose admission changed a security row, so the whole transaction rolls back.</summary>
    private sealed class SecurityStateSaveFault : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context && context.ChangeTracker.Entries<UserSecurityState>().Any(x => x.State == EntityState.Modified))
                throw new IOException("Synthetic save transport failure.");
            return ValueTask.FromResult(result);
        }
    }
}
