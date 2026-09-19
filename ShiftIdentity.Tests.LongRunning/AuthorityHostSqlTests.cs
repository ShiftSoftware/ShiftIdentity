using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Services;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// The authority as a host enables it: through <c>ShiftIdentityConfiguration.Authority</c> and the public dashboard
/// registration alone, with its hosted startup running for real on the fixture's database.
/// </summary>
[Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class AuthorityHostSqlTests(SqlIdentityFixture fixture) : IClassFixture<SqlIdentityFixture>, IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        await db.Apps.IgnoreQueryFilters().Where(x => x.AppId == ConfiguredIdentityHttpHost<IdentityTestDbContext>.ClientId).ExecuteDeleteAsync();
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task<JsonElement> LoginAsync(HttpClient client, string username, string password)
    {
        using var response = await client.PostAsJsonAsync("api/Auth/Login", new { Username = username, Password = password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Prop(await response.Content.ReadFromJsonAsync<JsonElement>(), "Entity");
    }

    // The deployed envelope is read case-insensitively, as the template's own tests read it.
    private static JsonElement Prop(JsonElement element, string name) =>
        element.EnumerateObject().First(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static string Claim(string token, string name) => new JsonWebToken(token).GetPayloadValue<string>(name);

    [Fact]
    public async Task Startup_readies_the_client_row_and_the_deployed_login_issues_a_v2_session_the_host_accepts()
    {
        using var host = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture);
        await using (var db = fixture.CreateContext())
        {
            var app = await db.Apps.SingleAsync(x => x.AppId == ConfiguredIdentityHttpHost<IdentityTestDbContext>.ClientId);
            Assert.Equal("Configured identity host", app.DisplayName);
            Assert.Equal(1, (await db.Set<AuthenticationPolicyState>().SingleAsync()).Revision);
        }
        var session = await LoginAsync(host.Client, fixture.Username, fixture.Password);
        var access = Prop(session, "Token").GetString()!;
        Assert.Equal("2", Claim(access, "shift_schema"));
        Assert.Equal(ConfiguredIdentityHttpHost<IdentityTestDbContext>.ClientId, Claim(access, "shift_client"));
        Assert.Equal("configured-api", Claim(access, "shift_resource"));
        Assert.Equal(fixture.Options.Issuer, Claim(access, "iss"));
        // The host's own bearer (the deployed AddShiftIdentity registration) accepts the session on a deployed route.
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using (var profile = await host.Client.GetAsync("api/UserManager/UserData"))
        {
            Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
            Assert.Equal(fixture.Username, Prop(Prop(await profile.Content.ReadFromJsonAsync<JsonElement>(), "Entity"), "Username").GetString());
        }
        // And the authority's own routes are mapped by the dashboard mapping.
        using var status = await host.Client.GetAsync("api/identity/v2/mfa");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal("authenticatorStatus", (await status.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("kind").GetString());
        // The deployed refresh renews it through the authority.
        using var renewed = await host.Client.PostAsJsonAsync("api/Auth/Refresh", new { RefreshToken = Prop(session, "RefreshToken").GetString() });
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        await using var audit = fixture.CreateContext();
        Assert.Contains(await audit.Set<AuthenticationAuditEvent>().Select(x => x.Outcome).ToListAsync(), x => x == "SessionIssued");
    }

    [Fact]
    public async Task Startup_creates_the_policy_row_from_configuration_when_it_is_missing()
    {
        await using (var db = fixture.CreateContext())
            await db.Set<AuthenticationPolicyState>().ExecuteDeleteAsync();
        using var host = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture, c => { c.MfaSettings.Mandatory = true; c.Authority.RequireVerifiedEmail = true; });
        await using var check = fixture.CreateContext();
        var policy = await check.Set<AuthenticationPolicyState>().SingleAsync();
        Assert.Equal(1, policy.Revision);
        Assert.True(policy.MfaEnabled); Assert.True(policy.MfaMandatory); Assert.True(policy.RequireVerifiedEmail);
    }

    [Fact]
    public async Task A_policy_change_advances_the_revision_at_the_next_start_and_ends_sessions_bound_to_the_old_one()
    {
        string refresh;
        using (var first = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture))
            refresh = Prop(await LoginAsync(first.Client, fixture.Username, fixture.Password), "RefreshToken").GetString()!;
        using (var second = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture, c => c.Authority.RequireVerifiedEmail = true))
        {
            await using (var db = fixture.CreateContext())
            {
                var policy = await db.Set<AuthenticationPolicyState>().SingleAsync();
                Assert.Equal(2, policy.Revision); Assert.True(policy.RequireVerifiedEmail);
            }
            using var renewed = await second.Client.PostAsJsonAsync("api/Auth/Refresh", new { RefreshToken = refresh });
            Assert.Equal(HttpStatusCode.BadRequest, renewed.StatusCode);
            // A fresh login is issued under the new revision.
            var session = await LoginAsync(second.Client, fixture.Username, fixture.Password);
            Assert.Equal("2", Claim(Prop(session, "Token").GetString()!, "shift_policy"));
        }
        // The same configuration again changes nothing.
        using var third = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture, c => c.Authority.RequireVerifiedEmail = true);
        await using var again = fixture.CreateContext();
        Assert.Equal(2, (await again.Set<AuthenticationPolicyState>().SingleAsync()).Revision);
        Assert.Equal(1, await again.Apps.IgnoreQueryFilters().CountAsync(x => x.AppId == ConfiguredIdentityHttpHost<IdentityTestDbContext>.ClientId));
    }

    [Fact]
    public async Task Users_that_existed_before_the_authority_get_their_security_row_at_startup_and_a_later_legacy_insert_is_refused_until_expanded()
    {
        var hash = HashService.GenerateHash(fixture.Password);
        long before;
        await using (var db = fixture.CreateContext())
            before = (await InsertLegacyUserAsync(db, "legacy-before", "before@example.invalid", verified: true, hash)).ID;
        using var host = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture);
        await using var users = fixture.CreateContext();
        // Startup created the row: version 1, lookup keys from the saved identifiers, the verified saved address
        // attested as legacy recovery data.
        var state = await users.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == before);
        Assert.Equal(1, state.SecurityVersion); Assert.Equal(1, state.FactorGeneration);
        Assert.Equal("BEFORE@EXAMPLE.INVALID", state.EmailLookupKey);
        Assert.Equal(RecoveryEmailProvenance.VerifiedLegacyMigration, state.RecoveryEmailProvenance);
        Assert.Equal("before@example.invalid", state.RecoveryEmail);
        // A user a legacy path inserts after startup (direct SQL here): the admission refuses instead of defaulting a row.
        var user = await InsertLegacyUserAsync(users, "legacy-after", "after@example.invalid", verified: false, hash);
        using (var refused = await host.Client.PostAsJsonAsync("api/Auth/Login", new { Username = user.Username, Password = fixture.Password }))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.False(await users.Set<UserSecurityState>().AnyAsync(x => x.UserID == user.ID));
        // The expansion the built-in seed and the live-data sync run (and the next start would run) creates the row.
        Assert.Equal(1, await UserSecurityExpansion.ExpandMissingAsync(users));
        Assert.Equal(0, await UserSecurityExpansion.ExpandMissingAsync(users));
        var session = await LoginAsync(host.Client, user.Username, fixture.Password);
        Assert.Equal("2", Claim(Prop(session, "Token").GetString()!, "shift_schema"));
        var created = await users.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == user.ID);
        Assert.Equal(1, created.SecurityVersion);
        Assert.Equal(user.Username.ToUpperInvariant(), created.UsernameLookupKey);
        Assert.Equal("AFTER@EXAMPLE.INVALID", created.EmailLookupKey);
        // An unverified saved address is not recovery data.
        Assert.Equal(RecoveryEmailProvenance.Unknown, created.RecoveryEmailProvenance);
        Assert.Null(created.RecoveryEmail);
    }

    [Fact]
    public async Task A_host_that_maps_identity_as_temporal_tables_copies_factors_at_startup_and_admits_logins()
    {
        // The template's DbContext maps the identity entities as temporal tables (UseTemporal), whose period columns
        // are hidden and therefore absent from a SELECT *: the store's hinted reads must name their columns.
        await using var temporal = new SqlIdentityFixture { Temporal = true, SeedLegacyFactorBeforeExpansion = true };
        await temporal.InitializeAsync();
        await using var db = temporal.CreateContext();
        Assert.True(db.Model.FindEntityType(typeof(App))!.IsTemporal());
        Assert.True(db.Model.FindEntityType(typeof(User))!.IsTemporal());
        Assert.Null((await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == temporal.UserID)).ProtectedTotpSecret);
        using var host = new ConfiguredIdentityHttpHost<TemporalIdentityTestDbContext>(temporal);
        // The startup visitor read the user through the hinted query and copied its factor.
        Assert.NotNull((await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == temporal.UserID)).ProtectedTotpSecret);
        // The deployed login admits through the hinted policy, client and state reads; with an enrolled factor it
        // answers with the MFA step credential of the deployed contract, not a refusal.
        using var response = await host.Client.PostAsJsonAsync("api/Auth/Login", new { Username = temporal.Username, Password = temporal.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(await db.Set<AuthenticationAuditEvent>().Select(x => x.Outcome).ToListAsync(), x => x == "LegacyLoginPasswordProven");
    }

    private async Task<User> InsertLegacyUserAsync(ShiftSoftware.ShiftIdentity.Data.ShiftIdentityDbContext db, string prefix, string email, bool verified, HashModel hash)
    {
        var template = await db.Users.AsNoTracking().SingleAsync(x => x.ID == fixture.UserID);
        var user = new User
        {
            Username = prefix + "-" + Guid.NewGuid().ToString("N")[..8], FullName = "Legacy Insert", IsActive = true,
            Email = email, EmailVerified = verified, PasswordHash = hash.PasswordHash, Salt = hash.Salt,
            CompanyID = template.CompanyID, CompanyBranchID = template.CompanyBranchID, CountryID = template.CountryID, RegionID = template.RegionID
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return user;
    }

    [Fact]
    public async Task A_host_that_leaves_the_authority_off_keeps_the_previous_issuer_and_maps_no_v2_route()
    {
        using var host = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture, enabled: false);
        using (var scope = host.Services.CreateScope())
            Assert.Null(scope.ServiceProvider.GetService<IUserAccountAuthority>());
        using (var v2 = await host.Client.PostAsJsonAsync("api/identity/v2/login", new PasswordLoginRequest(fixture.Username, fixture.Password, "challenge")))
            Assert.Equal(HttpStatusCode.NotFound, v2.StatusCode);
        var session = await LoginAsync(host.Client, fixture.Username, fixture.Password);
        var token = new JsonWebToken(Prop(session, "Token").GetString()!);
        Assert.False(token.TryGetPayloadValue<string>("shift_schema", out _));
        await using var db = fixture.CreateContext();
        Assert.False(await db.Apps.IgnoreQueryFilters().AnyAsync(x => x.AppId == ConfiguredIdentityHttpHost<IdentityTestDbContext>.ClientId));
        Assert.Empty(await db.Set<AuthenticationAuditEvent>().ToListAsync());
    }

    [Fact]
    public async Task A_pending_migration_stops_startup_with_guidance()
    {
        await using var pending = new SqlIdentityFixture();
        await pending.InitializeAsync();
        await using (var db = pending.CreateContext())
            await db.Database.ExecuteSqlRawAsync("DROP TABLE [ShiftIdentity].[AuthenticationPolicyStates]");
        var error = Assert.Throws<InvalidOperationException>(() => new ConfiguredIdentityHttpHost<IdentityTestDbContext>(pending));
        Assert.Contains("migration", error.Message);
    }
}
