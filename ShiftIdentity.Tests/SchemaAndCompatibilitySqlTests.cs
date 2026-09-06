using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using OtpNet;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Collection("Identity SQL")]
[Trait("Category", "Sql")]
public sealed class SchemaAndCompatibilitySqlTests(SqlIdentityFixture fixture)
{

    [Fact]
    public async Task Validly_signed_refresh_with_another_subject_cannot_rebind_to_current_user()
    {
        fixture.Clock = TimeProvider.System;
        await fixture.ResetAsync();
        var client = new AuthenticationClient("test-client", "test-api");
        var proof = new ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.SessionProof(
            fixture.UserID, 1, 1, 1, false, fixture.Clock.GetUtcNow(), client.ID, client.Audience, false, "another-subject");
        var codec = new ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.AdmissionTokenCodec(fixture.Options, fixture.Clock);
        var token = codec.Issue(new(proof, fixture.Username, "Synthetic User", [], fixture.Clock.GetUtcNow()));
        using var host = new IdentityHttpHost(fixture, client);
        Assert.Equal(AuthenticationFailure.InvalidGrant, Assert.IsType<AuthenticationRefused>(
            await host.RefreshAsync(token.RefreshToken)).Code);
    }


    [Fact]
    public async Task Ordinary_access_is_stateless_until_expiry_but_cannot_complete_mfa_or_renew_after_revocation()
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        fixture.Clock = clock;
        try
        {
            await fixture.ResetAsync();
            using var host = new IdentityHttpHost(fixture);
            var session = Assert.IsType<SessionIssued>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge));
            host.Client.DefaultRequestHeaders.Authorization = new("Bearer", session.Session.Token);
            using var initial = await host.Client.GetAsync("/fixture/resource");
            Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
            using var mfa = await host.Client.PostAsJsonAsync("/api/identity/v2/login/mfa",
                new CompleteMfaRequest("123456", IdentityHttpHost.Pkce().Verifier));
            Assert.Equal(HttpStatusCode.Unauthorized, mfa.StatusCode);
            await using (var db = fixture.CreateContext())
                await db.Set<UserSecurityState>().ExecuteUpdateAsync(x => x.SetProperty(y => y.SecurityVersion, y => y.SecurityVersion + 1));
            Assert.IsType<AuthenticationRefused>(await host.RefreshAsync(session.Session.RefreshToken));
            using var residual = await host.Client.GetAsync("/fixture/resource");
            Assert.Equal(HttpStatusCode.OK, residual.StatusCode);
            clock.Advance(TimeSpan.FromMinutes(15));
            using var expired = await host.Client.GetAsync("/fixture/resource");
            Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        }
        finally { fixture.Clock = TimeProvider.System; }
    }

    [Fact]
    public async Task Migration_backfills_plain_non_temporal_state_and_persists_rowversion()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(UserSecurityState), typeof(AuthenticationOperation), typeof(AuthenticationPolicyState), typeof(AuthenticationAuditEvent) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.False(entity.IsTemporal());
            Assert.Null(entity.BaseType);
        }
        var state = await db.Set<UserSecurityState>().SingleAsync();
        Assert.Equal(1, state.SecurityVersion);
        var rowVersion = state.RowVersion.ToArray();
        state.SecurityVersion++;
        await db.SaveChangesAsync();
        Assert.NotEqual(rowVersion, state.RowVersion);
        await using var legacy = new LegacyIdentityTestDbContext(fixture.BuildOptions());
        Assert.Null(legacy.Model.FindEntityType(typeof(UserSecurityState)));
        Assert.Null(legacy.Model.FindEntityType(typeof(AuthenticationOperation)));
    }

    [Fact]
    public async Task Missing_schema_refuses_without_returning_credentials()
    {
        // Separate fixture: never removes schema in a shared test collection database.
        await using var missing = new SqlIdentityFixture();
        await missing.InitializeAsync();
        await missing.ResetAsync(mfa: true);
        await using (var db = missing.CreateContext())
            await db.Database.ExecuteSqlRawAsync("DROP TABLE [ShiftIdentity].[AuthenticationOperations]");
        using var host = new IdentityHttpHost(missing);
        Assert.Equal(AuthenticationFailure.Unavailable, Assert.IsType<AuthenticationRefused>(
            await host.LoginAsync(missing, IdentityHttpHost.Pkce().Challenge)).Code);
    }

    [Fact]
    public async Task Configured_totp_digits_period_and_window_are_used()
    {
        fixture.Clock = TimeProvider.System;
        await fixture.ResetAsync(mfa: true);
        await using (var db = fixture.CreateContext())
        {
            var policy = await db.Set<AuthenticationPolicyState>().SingleAsync();
            policy.TotpDigits = 8; policy.TotpPeriodSeconds = 60; policy.TotpWindowPast = 0; policy.TotpWindowFuture = 0;
            await db.SaveChangesAsync();
        }
        using var host = new IdentityHttpHost(fixture);
        var pkce = IdentityHttpHost.Pkce();
        var operation = Assert.IsType<ChallengeRequired>(await host.LoginAsync(fixture, pkce.Challenge));
        var code = new Totp(fixture.FactorSecret, step: 60, totpSize: 8).ComputeTotp(fixture.Clock.GetUtcNow().UtcDateTime);
        Assert.IsType<SessionIssued>(await host.CompleteAsync(operation.Challenge.Handle!, code, pkce.Verifier));
    }

    [Fact]
    public async Task Production_registration_keeps_existing_login_refresh_contract_and_no_v2_routes()
    {
        await fixture.ResetAsync();
        using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture);
        using var login = await host.Client.PostAsJsonAsync("/api/Auth/Login", new LoginDTO { Username = fixture.Username, Password = fixture.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var envelope = await login.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>();
        Assert.False(string.IsNullOrWhiteSpace(envelope!.Entity!.Token));
        using var refresh = await host.Client.PostAsJsonAsync("/api/Auth/Refresh", new { RefreshToken = envelope.Entity.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Contains("no-store", refresh.Headers.CacheControl!.ToString());
        using var badPassword = await host.Client.PostAsJsonAsync("/api/Auth/Login", new LoginDTO { Username = fixture.Username, Password = "wrong" });
        Assert.Equal(HttpStatusCode.BadRequest, badPassword.StatusCode);
        using var staged = await host.Client.PostAsJsonAsync("/api/identity/v2/login", new PasswordLoginRequest(fixture.Username, fixture.Password, IdentityHttpHost.Pkce().Challenge));
        Assert.Equal(HttpStatusCode.NotFound, staged.StatusCode);
    }
}
