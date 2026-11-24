using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftIdentity.Tests.Infrastructure;
using Xunit;

namespace ShiftIdentity.Tests.Sqlite;

/// <summary>
/// The security store on a database other than SQL Server: every read that takes lock hints on SQL Server reads the
/// same row with plain EF, the named lock of the delivery throttle is skipped, and a duplicate key is recognised by
/// reading the key again instead of by SQL Server's error numbers.
/// </summary>
public sealed class SqliteSecurityStoreTests : IAsyncLifetime
{
    private static readonly AuthenticationClient Client = new("test-client", "test-audience");
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private SqliteIdentityTestDbContext db = null!;
    private SqlIdentitySecurityStore store = null!;
    private long aliceID, bobID;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await connection.OpenAsync(Ct);
        db = new SqliteIdentityTestDbContext(new DbContextOptionsBuilder<SqliteIdentityTestDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(Ct);

        var country = new Country { Name = "Country", CallingCode = "+1" };
        var region = new Region { Name = "Region", Country = country };
        var city = new City { Name = "City", Region = region };
        var company = new Company { Name = "Company" };
        var branch = new CompanyBranch { Name = "Branch", Company = company, Region = region, City = city };
        db.Add(branch);
        db.Apps.AddRange(new App { AppId = "test-client", DisplayName = "Test", RedirectUri = "https://client.invalid/callback" },
            new App { AppId = "other-client", DisplayName = "Other", RedirectUri = "https://other.invalid/callback" });
        db.Set<AuthenticationPolicyState>().Add(new());
        await db.SaveChangesAsync(Ct);

        var alice = NewUser("alice", "alice@example.com", branch);
        var bob = NewUser("bob", "bob@example.com", branch);
        db.Users.AddRange(alice, bob);
        await db.SaveChangesAsync(Ct);
        db.Set<UserSecurityState>().AddRange(UserSecurityExpansion.CreateFor(alice), UserSecurityExpansion.CreateFor(bob));
        await db.SaveChangesAsync(Ct);
        (aliceID, bobID) = (alice.ID, bob.ID);

        db.ChangeTracker.Clear();
        store = new SqlIdentitySecurityStore(db);
    }

    public async ValueTask DisposeAsync()
    {
        await db.DisposeAsync();
        await connection.DisposeAsync();
    }

    private static User NewUser(string username, string? email, CompanyBranch branch) => new()
    {
        Username = username, Email = email, FullName = username, IsActive = true, PasswordHash = new byte[64], Salt = new byte[64],
        CompanyBranchID = branch.ID, CompanyID = branch.CompanyID, CountryID = branch.Region!.CountryID, RegionID = branch.RegionID,
    };

    [Fact]
    public void The_database_is_not_SQL_Server() => Assert.False(db.Database.IsSqlServer());

    [Fact]
    public async Task Admission_reads_the_policy_the_client_app_and_the_users_own_security_row()
    {
        var read = await store.AdmitAsync(bobID, null, Client, unit =>
            Task.FromResult((unit.User.ID, unit.Security.UserID, unit.Policy.ID, unit.App?.AppId)), Ct);

        Assert.Equal((bobID, bobID, 1, "test-client"), read);
    }

    [Fact]
    public async Task Admission_saves_the_change_the_transition_made()
    {
        await store.AdmitAsync(aliceID, null, Client, unit => { unit.Security.FailedProofs = 3; return Task.FromResult(0); }, Ct);

        Assert.Equal(3, (await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == aliceID, Ct)).FailedProofs);
        Assert.Equal(0, (await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == bobID, Ct)).FailedProofs);
    }

    [Fact]
    public async Task An_unknown_client_is_refused_as_on_SQL_Server()
    {
        await Assert.ThrowsAsync<IdentitySecurityConflictException>(() =>
            store.AdmitAsync(aliceID, null, new AuthenticationClient("no-such-client", "x"), _ => Task.FromResult(0), Ct));
    }

    [Fact]
    public async Task The_identifier_check_reads_both_lookup_keys()
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, Ct);

        Assert.True(await store.IdentifierInUseAsync("BOB", aliceID, Ct));                 // bob's username key
        Assert.True(await store.IdentifierInUseAsync(" bob@example.com", aliceID, Ct));    // bob's email key
        Assert.False(await store.IdentifierInUseAsync("bob", bobID, Ct));                  // bob's own
        Assert.False(await store.IdentifierInUseAsync("carol", aliceID, Ct));
    }

    [Fact]
    public async Task The_security_email_recheck_finds_the_one_account()
    {
        var lookup = await store.ResolveSecurityEmailAsync("alice@example.com", Ct);
        Assert.Equal(aliceID, lookup?.UserID);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, Ct);
        Assert.True(await store.RecheckSecurityEmailAsync(lookup!, Ct));
        Assert.False(await store.RecheckSecurityEmailAsync(lookup! with { UserID = bobID }, Ct));
    }

    [Fact]
    public async Task The_legacy_factor_migration_reads_the_user_row()
    {
        await db.Users.Where(x => x.ID == bobID).ExecuteUpdateAsync(x => x.SetProperty(u => u.TotpSecret, Array.Empty<byte>()), Ct);

        var batch = await store.MigrateLegacyTotpBatchAsync(0, 10, (_, _) => { }, Ct);

        Assert.Equal((1, 0, 1), (batch.Examined, batch.Copied, batch.Cleared));
        Assert.Null((await db.Users.AsNoTracking().SingleAsync(x => x.ID == bobID, Ct)).TotpSecret);
    }

    [Fact]
    public async Task The_provider_link_read_matches_all_three_columns()
    {
        var now = DateTimeOffset.UtcNow;
        UserProviderLink Link(long userID, SignInProvider provider, string tenant, string objectID) => new()
        {
            ID = Guid.NewGuid(), UserID = userID, Provider = provider, TenantID = tenant, ObjectID = objectID,
            EmailLookupKey = "KEY", Email = "x@example.com", CreatedAt = now, LastUsedAt = now,
        };
        db.Set<UserProviderLink>().AddRange(Link(aliceID, SignInProvider.Microsoft, "tenant", "object"),
            Link(bobID, SignInProvider.Microsoft, "tenant", "other-object"),
            Link(bobID, SignInProvider.Microsoft, "other-tenant", "object"));
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, Ct);
        Assert.Equal(aliceID, (await store.ReadProviderLinkAsync(SignInProvider.Microsoft, "tenant", "object", Ct))?.UserID);
        Assert.Null(await store.ReadProviderLinkAsync(SignInProvider.Microsoft, "tenant", "missing", Ct));
    }

    [Fact]
    public async Task The_delivery_limit_counts_without_the_named_lock()
    {
        var now = DateTimeOffset.UtcNow;
        var window = TimeSpan.FromMinutes(1);

        Assert.True(await store.ConsumeIngressAsync("key", now, 2, window, Ct));
        Assert.True(await store.ConsumeIngressAsync("key", now, 2, window, Ct));
        Assert.False(await store.ConsumeIngressAsync("key", now, 2, window, Ct));
        Assert.True(await store.ConsumeIngressAsync("another-key", now, 2, window, Ct));
        Assert.True(await store.IngressExhaustedAsync("key", now, 2, window, Ct));
    }

    private static DeviceAuthorization Device(byte[] userCode) => new()
    {
        ID = Guid.NewGuid(), DeviceCodeDigest = Digest(), UserCodeDigest = userCode, ClientID = "tv", Audience = "test-audience",
        State = DeviceAuthorizationState.Pending, CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10), Interval = 5,
    };

    private static byte[] Digest() => Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray();

    [Fact]
    public async Task A_device_user_code_already_held_is_the_handled_duplicate()
    {
        var userCode = Digest();
        var first = Device(userCode);

        Assert.True(await store.AddDeviceAuthorizationAsync(first, Ct));
        Assert.False(await store.AddDeviceAuthorizationAsync(Device(userCode), Ct));
        Assert.Equal(first.ID, (await store.FindDeviceAuthorizationAsync(userCode, Ct))?.ID);
    }

    [Fact]
    public async Task A_device_row_that_fails_for_another_reason_is_not_taken_for_a_duplicate()
    {
        var broken = Device(Digest());
        broken.ClientID = null!;   // NOT NULL: a failure that is not a duplicate key

        var refused = await Assert.ThrowsAsync<IdentitySecurityUnavailableException>(() => store.AddDeviceAuthorizationAsync(broken, Ct));
        Assert.IsType<DbUpdateException>(refused.InnerException);
    }

    [Fact]
    public async Task A_device_sign_in_is_read_for_change_by_its_ID()
    {
        var device = Device(Digest());
        var other = Device(Digest());
        Assert.True(await store.AddDeviceAuthorizationAsync(device, Ct));
        Assert.True(await store.AddDeviceAuthorizationAsync(other, Ct));

        var seen = await store.AdmitDeviceAuthorizationAsync(device.ID, row => { row!.Interval = 10; return row.ID; }, Ct);

        Assert.Equal(device.ID, seen);
        Assert.Equal(10, (await store.ReadDeviceAuthorizationAsync(device.ID, Ct))!.Interval);
        Assert.Equal(5, (await store.ReadDeviceAuthorizationAsync(other.ID, Ct))!.Interval);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, Ct);
        Assert.Equal(other.ID, (await store.LockDeviceAuthorizationAsync(other.ID, Ct))?.ID);
    }

    [Fact]
    public async Task By_default_a_username_and_an_email_match_with_case_as_SQLite_compares()
    {
        var branch = await db.Set<CompanyBranch>().AsNoTracking().Include(x => x.Region).FirstAsync(Ct);
        db.Users.Add(NewUser("Dave", "Dave@Example.com", branch));
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();

        Assert.True(await db.Users.AnyAsync(x => x.Username == "Dave", Ct));
        Assert.False(await db.Users.AnyAsync(x => x.Username == "DAVE", Ct));
        Assert.False(await db.Users.AnyAsync(x => x.Email == "dave@example.com", Ct));
    }

    [Fact]
    public async Task With_UseCaseInsensitiveText_a_username_and_an_email_match_in_any_case()
    {
        await using var other = new SqliteConnection("Data Source=:memory:");
        await other.OpenAsync(Ct);
        await using var noCase = new SqliteIdentityTestDbContext(
            new DbContextOptionsBuilder<SqliteIdentityTestDbContext>().UseSqlite(other, sqlite => sqlite.UseCaseInsensitiveText()).Options);
        await noCase.Database.EnsureCreatedAsync(Ct);
        var country = new Country { Name = "Country", CallingCode = "+1" };
        var region = new Region { Name = "Region", Country = country };
        var branch = new CompanyBranch { Name = "Branch", Company = new Company { Name = "Company" }, Region = region, City = new City { Name = "City", Region = region } };
        noCase.Add(branch);
        await noCase.SaveChangesAsync(Ct);
        noCase.Users.Add(NewUser("Dave", "Dave@Example.com", branch));
        await noCase.SaveChangesAsync(Ct);
        noCase.ChangeTracker.Clear();

        Assert.True(await noCase.Users.AnyAsync(x => x.Username == "DAVE", Ct));
        Assert.True(await noCase.Users.AnyAsync(x => x.Email == "dave@example.com", Ct));
        noCase.Users.Add(NewUser("dave", null, branch));
        await Assert.ThrowsAsync<DbUpdateException>(() => noCase.SaveChangesAsync(Ct));   // unique without case, as on SQL Server
    }

    [Fact]
    public async Task Security_rows_are_created_and_a_lookup_key_collision_keeps_the_row_without_keys()
    {
        var branch = await db.Set<CompanyBranch>().AsNoTracking().Include(x => x.Region).FirstAsync(Ct);
        var carol = NewUser("carol", null, branch);
        var carolAgain = NewUser("CAROL", null, branch);   // a different username with the same lookup key
        db.Users.AddRange(carol, carolAgain);
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
        var degraded = new List<long>();

        var created = await UserSecurityExpansion.ExpandMissingAsync(db, degraded.Add, Ct);

        Assert.Equal(2, created);
        Assert.Equal([carolAgain.ID], degraded);
        var rows = await db.Set<UserSecurityState>().AsNoTracking().Where(x => x.UserID == carol.ID || x.UserID == carolAgain.ID).ToListAsync(Ct);
        Assert.Equal("CAROL", rows.Single(x => x.UserID == carol.ID).UsernameLookupKey);
        Assert.Null(rows.Single(x => x.UserID == carolAgain.ID).UsernameLookupKey);
        Assert.Equal(0, await UserSecurityExpansion.ExpandMissingAsync(db, degraded.Add, Ct));
    }
}
