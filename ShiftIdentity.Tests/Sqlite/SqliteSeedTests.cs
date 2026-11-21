using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Seeding;
using Xunit;

namespace ShiftIdentity.Tests.Sqlite;

/// <summary>
/// The SQLite seed (<c>SeedShiftIdentitySqliteAsync</c>): both overloads write the same rows, a seed on another
/// database is skipped with a warning, explicit IDs are kept, a second call matches instead of inserting, a bad seed
/// writes nothing, and a seeded user signs in to a real identity host.
/// </summary>
public sealed class SqliteSeedTests : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public async ValueTask InitializeAsync()
    {
        await connection.OpenAsync(Ct);
        await using var db = Context();
        await db.Database.EnsureCreatedAsync(Ct);
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();

    private SqliteIdentityTestDbContext Context(SqliteConnection? on = null) =>
        new(new DbContextOptionsBuilder<SqliteIdentityTestDbContext>().UseSqlite(on ?? connection).Options);

    /// <summary>A generic host whose only service is the identity context: what the seed extension needs.</summary>
    private static IHost HostOn(Func<ShiftIdentityDbContext> context, LogSink? log = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        if (log is not null) builder.Logging.AddProvider(log);
        builder.Services.AddScoped(_ => context());
        return builder.Build();
    }

    private const string FullJson = """
        {
          "updateExisting": false,
          "countries":       [{ "key": "iq", "name": "Iraq", "shortCode": "IQ", "callingCode": "+964" }],
          "regions":         [{ "key": "krg", "name": "Kurdistan", "country": "iq" }],
          "cities":          [{ "key": "erbil", "name": "Erbil", "region": "krg" }],
          "companies":       [{ "key": "shift", "name": "Shift Software", "shortCode": "SFT", "companyType": "NotSpecified" },
                              { "key": "shift-sub", "name": "Shift Sub", "parentCompany": "shift" }],
          "brands":          [{ "key": "toyota", "name": "Toyota" }],
          "departments":     [{ "key": "sales", "name": "Sales" }],
          "services":        [{ "key": "repair", "name": "Repair" }],
          "companyBranches": [{ "key": "erbil-hq", "name": "Erbil HQ", "company": "shift", "city": "erbil",
                               "brands": ["toyota"], "departments": ["sales"], "services": ["repair"] }],
          "teams":           [{ "key": "support", "name": "Support", "company": "shift", "companyBranches": ["erbil-hq"], "tags": ["first-line"] }],
          "accessTrees":     [{ "key": "admin", "name": "Admin", "fullAccess": true },
                              { "key": "viewer", "name": "Viewer", "tree": { "ShiftIdentityActions": [1] } }],
          "apps":            [{ "appId": "demo-app", "displayName": "Demo App", "redirectUri": "http://localhost:5234/Auth/Token" }],
          "users":           [{ "id": 1, "username": "demo", "password": "OneTwo", "fullName": "Demo", "email": "demo@example.com",
                               "emailVerified": true, "companyBranch": "erbil-hq", "accessTrees": ["admin"], "teams": ["support"] }],
          "companyCalendars": [{ "key": "ramadan", "title": "Ramadan", "company": "shift", "startDate": "2026-02-18", "endDate": "2026-03-19",
                                "entryType": "Holiday", "companyBranches": ["erbil-hq"],
                                "weekendGroups": [{ "brands": ["toyota"], "weekends": [{ "day": 5, "repeat": 1, "skip": 0 }] }] }]
        }
        """;

    private static IdentitySeedData FullObject() => new()
    {
        Countries = [new() { Key = "iq", Name = "Iraq", ShortCode = "IQ", CallingCode = "+964" }],
        Regions = [new() { Key = "krg", Name = "Kurdistan", Country = "iq" }],
        Cities = [new() { Key = "erbil", Name = "Erbil", Region = "krg" }],
        Companies = [new() { Key = "shift", Name = "Shift Software", ShortCode = "SFT" }, new() { Key = "shift-sub", Name = "Shift Sub", ParentCompany = "shift" }],
        Brands = [new() { Key = "toyota", Name = "Toyota" }],
        Departments = [new() { Key = "sales", Name = "Sales" }],
        Services = [new() { Key = "repair", Name = "Repair" }],
        CompanyBranches = [new() { Key = "erbil-hq", Name = "Erbil HQ", Company = "shift", City = "erbil", Brands = ["toyota"], Departments = ["sales"], Services = ["repair"] }],
        Teams = [new() { Key = "support", Name = "Support", Company = "shift", CompanyBranches = ["erbil-hq"], Tags = ["first-line"] }],
        AccessTrees = [new() { Key = "admin", Name = "Admin", FullAccess = true }, new() { Key = "viewer", Name = "Viewer", Tree = """{"ShiftIdentityActions":[1]}""" }],
        Apps = [new() { AppId = "demo-app", DisplayName = "Demo App", RedirectUri = "http://localhost:5234/Auth/Token" }],
        Users = [new() { ID = 1, Username = "demo", Password = "OneTwo", FullName = "Demo", Email = "demo@example.com", EmailVerified = true,
            CompanyBranch = "erbil-hq", AccessTrees = ["admin"], Teams = ["support"] }],
        CompanyCalendars = [new() { Key = "ramadan", Title = "Ramadan", Company = "shift", StartDate = new(2026, 2, 18), EndDate = new(2026, 3, 19),
            EntryType = ShiftSoftware.ShiftIdentity.Core.Enums.CalendarEntryType.Holiday, CompanyBranches = ["erbil-hq"],
            WeekendGroups = [new() { Brands = ["toyota"], Weekends = [new() { Day = 5, Repeat = 1, Skip = 0 }] }] }],
    };

    /// <summary>Every seeded row, as text, in a stable order: two databases seeded alike give equal snapshots.</summary>
    private static async Task<List<string>> SnapshotAsync(ShiftIdentityDbContext db)
    {
        var rows = new List<string>();
        rows.AddRange(await db.Countries.OrderBy(x => x.ID).Select(x => $"country {x.ID} {x.Name} {x.ShortCode} {x.CallingCode}").ToListAsync(Ct));
        rows.AddRange(await db.Regions.OrderBy(x => x.ID).Select(x => $"region {x.ID} {x.Name} {x.CountryID}").ToListAsync(Ct));
        rows.AddRange(await db.Cities.OrderBy(x => x.ID).Select(x => $"city {x.ID} {x.Name} {x.RegionID} {x.CountryID}").ToListAsync(Ct));
        rows.AddRange(await db.Companies.OrderBy(x => x.ID).Select(x => $"company {x.ID} {x.Name} {x.ParentCompanyID} {x.CompanyType}").ToListAsync(Ct));
        rows.AddRange(await db.CompanyBranches.OrderBy(x => x.ID).Select(x => $"branch {x.ID} {x.Name} {x.CompanyID} {x.CityID} {x.RegionID} {x.CountryID}").ToListAsync(Ct));
        rows.AddRange(await db.CompanyBranchBrands.OrderBy(x => x.ID).Select(x => $"branch-brand {x.CompanyBranchID} {x.BrandID}").ToListAsync(Ct));
        rows.AddRange(await db.CompanyBranchDepartments.OrderBy(x => x.ID).Select(x => $"branch-department {x.CompanyBranchID} {x.DepartmentID}").ToListAsync(Ct));
        rows.AddRange(await db.CompanyBranchServices.OrderBy(x => x.ID).Select(x => $"branch-service {x.CompanyBranchID} {x.ServiceID}").ToListAsync(Ct));
        rows.AddRange((await db.Teams.OrderBy(x => x.ID).ToListAsync(Ct)).Select(x => $"team {x.ID} {x.Name} {x.CompanyID} {string.Join(",", x.Tags)}"));
        rows.AddRange(await db.TeamCompanyBranches.OrderBy(x => x.ID).Select(x => $"team-branch {x.TeamID} {x.CompanyBranchID}").ToListAsync(Ct));
        rows.AddRange(await db.TeamUsers.OrderBy(x => x.ID).Select(x => $"team-user {x.TeamID} {x.UserID}").ToListAsync(Ct));
        rows.AddRange(await db.AccessTrees.OrderBy(x => x.ID).Select(x => $"access-tree {x.ID} {x.Name} {x.Tree}").ToListAsync(Ct));
        rows.AddRange(await db.Apps.OrderBy(x => x.ID).Select(x => $"app {x.ID} {x.AppId} {x.DisplayName} {x.RedirectUri}").ToListAsync(Ct));
        rows.AddRange(await db.Users.OrderBy(x => x.ID).Select(x => $"user {x.ID} {x.Username} {x.FullName} {x.Email} {x.EmailVerified} {x.CompanyBranchID} {x.CompanyID} {x.RegionID} {x.CountryID}").ToListAsync(Ct));
        rows.AddRange(await db.UserAccessTrees.OrderBy(x => x.ID).Select(x => $"user-tree {x.UserID} {x.AccessTreeID}").ToListAsync(Ct));
        rows.AddRange(await db.Set<UserSecurityState>().OrderBy(x => x.UserID).Select(x => $"security {x.UserID} {x.UsernameLookupKey} {x.EmailLookupKey} {x.RecoveryEmailProvenance}").ToListAsync(Ct));
        rows.AddRange((await db.CompanyCalendars.OrderBy(x => x.ID).ToListAsync(Ct)).Select(x =>
            $"calendar {x.ID} {x.Title} {x.CompanyID} {x.StartDate:yyyy-MM-dd} {x.EndDate:yyyy-MM-dd} {x.EntryType} {string.Join(",", x.WeekendGroups.SelectMany(g => g.BrandIds))}"));
        rows.AddRange(await db.CompanyCalendarBranches.OrderBy(x => x.Id).Select(x => $"calendar-branch {x.CompanyCalendarID} {x.CompanyBranchID}").ToListAsync(Ct));
        return rows;
    }

    [Fact]
    public async Task The_object_and_the_JSON_overloads_write_the_same_rows()
    {
        await using var other = new SqliteConnection("Data Source=:memory:");
        await other.OpenAsync(Ct);
        await using (var db = Context(other)) await db.Database.EnsureCreatedAsync(Ct);

        using var fromObject = HostOn(() => Context());
        using var fromJson = HostOn(() => Context(other));
        var objectResult = await fromObject.SeedShiftIdentitySqliteAsync(FullObject(), Ct);
        var jsonResult = await fromJson.SeedShiftIdentitySqliteAsync(FullJson, Ct);

        await using var a = Context();
        await using var b = Context(other);
        var rows = await SnapshotAsync(a);
        Assert.Equal(rows, await SnapshotAsync(b));
        Assert.Equal(objectResult.Tables, jsonResult.Tables);
        Assert.False(objectResult.Skipped);

        // What the seed asked for is there: links, a parent company, the security row, the full-access tree.
        Assert.Contains(rows, x => x.StartsWith("branch-brand ") && x.EndsWith(" 1"));
        Assert.Contains(rows, x => x.StartsWith("company 2 Shift Sub 1"));
        Assert.Contains(rows, x => x == "team-user 1 1");
        Assert.Contains(rows, x => x == "user-tree 1 1");
        Assert.Contains(rows, x => x == "calendar-branch 1 1");
        Assert.Contains(rows, x => x == "security 1 DEMO DEMO@EXAMPLE.COM VerifiedLegacyMigration");
        Assert.Contains(rows, x => x == "user 1 demo Demo demo@example.com True 1 1 1 1");
        Assert.Contains(rows, x => x == "access-tree 2 Viewer {\"ShiftIdentityActions\":[1]}");
        Assert.Equal(FullAccessTree.BuildJson([typeof(ShiftIdentityActions)]), (await a.AccessTrees.SingleAsync(x => x.Name == "Admin", Ct)).Tree);
        Assert.Equal(new IdentitySeedTableCount(1, 0), objectResult.Tables["Users"]);
        Assert.Equal(2, objectResult.Tables["Companies"].Inserted);
    }

    [Fact]
    public async Task On_a_database_that_is_not_SQLite_nothing_is_seeded_and_a_warning_says_so()
    {
        var log = new LogSink();
        // Never connects: the seed reads only the provider name.
        using var host = HostOn(() => new IdentityTestDbContext(new DbContextOptionsBuilder().UseSqlServer("Server=unused.invalid;Database=unused").Options), log);

        var result = await host.SeedShiftIdentitySqliteAsync(FullObject(), Ct);

        Assert.True(result.Skipped);
        Assert.Empty(result.Tables);
        var warning = Assert.Single(log.Entries, x => x.Level == LogLevel.Warning);
        Assert.Contains("Microsoft.EntityFrameworkCore.SqlServer", warning.Message);
        Assert.Contains("nothing was seeded", warning.Message);
        Assert.Contains("SeedDBAsync", warning.Message);
    }

    [Fact]
    public async Task Explicit_IDs_are_kept_and_generated_IDs_continue_after_them()
    {
        await using var db = Context();
        await new IdentitySeeder(db, []).SeedAsync(new IdentitySeedData
        {
            Countries = [new() { ID = 50, Key = "a", Name = "A", CallingCode = "+1" }, new() { Key = "b", Name = "B", CallingCode = "+2" }],
            Regions = [new() { ID = 7, Name = "R", Country = "a" }],
        }, Ct);

        Assert.Equal([50L, 51L], await db.Countries.OrderBy(x => x.ID).Select(x => x.ID).ToListAsync(Ct));
        Assert.Equal(50L, (await db.Regions.SingleAsync(x => x.ID == 7, Ct)).CountryID);
    }

    [Fact]
    public async Task On_a_file_a_second_call_inserts_nothing_and_UpdateExisting_updates_the_matched_rows()
    {
        var file = Path.Combine(Path.GetTempPath(), $"identity-seed-{Guid.NewGuid():N}.db");
        try
        {
            ShiftIdentityDbContext OnFile() => new IdentityTestDbContext(new DbContextOptionsBuilder().UseSqlite($"Data Source={file}").Options);
            await using (var db = OnFile()) await db.Database.EnsureCreatedAsync(Ct);
            using var host = HostOn(OnFile);

            var first = await host.SeedShiftIdentitySqliteAsync(FullObject(), Ct);
            List<string> before;
            await using (var db = OnFile()) before = await SnapshotAsync(db);
            SqliteConnection.ClearAllPools();

            var second = await host.SeedShiftIdentitySqliteAsync(FullJson, Ct);
            await using (var db = OnFile()) Assert.Equal(before, await SnapshotAsync(db));
            Assert.Equal(0, second.Inserted + second.Updated);
            Assert.True(first.Inserted > 0);

            // UpdateExisting: the matched rows take the seed's values; the user's new email gets new lookup keys and ends its sessions.
            var update = FullObject();
            update.UpdateExisting = true;
            update.Countries[0].ShortCode = "IRQ";
            update.Users[0].Email = "demo@shift.example";
            update.Users[0].Password = null;
            update.CompanyBranches[0].Brands = [];
            var third = await host.SeedShiftIdentitySqliteAsync(update, Ct);

            await using var after = OnFile();
            Assert.Equal("IRQ", (await after.Countries.SingleAsync(Ct)).ShortCode);
            Assert.Empty(await after.CompanyBranchBrands.ToListAsync(Ct));
            var security = await after.Set<UserSecurityState>().SingleAsync(Ct);
            Assert.Equal("DEMO@SHIFT.EXAMPLE", security.EmailLookupKey);
            Assert.Equal(2, security.SecurityVersion);
            Assert.Equal(2, security.ContactRevision);
            Assert.Equal(0, third.Inserted);
            Assert.Equal(new IdentitySeedTableCount(0, 1), third.Tables["Users"]);
            // No new password: the old one still stands.
            var user = await after.Users.SingleAsync(Ct);
            Assert.True(HashService.VerifyPassword("OneTwo", user.Salt, user.PasswordHash));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(file);
        }
    }

    [Fact]
    public async Task A_seed_with_problems_reports_every_one_and_writes_nothing()
    {
        await using var db = Context();
        var seed = new IdentitySeedData
        {
            Countries = [new() { Key = "iq", Name = "Iraq", CallingCode = "+964" }, new() { Key = "iq", Name = "Again", CallingCode = "+1" }],
            Companies = [new() { Key = "a", Name = "A", ParentCompany = "b" }, new() { Key = "b", Name = "B", ParentCompany = "a" }],
            CompanyBranches = [new() { Key = "hq", Name = "HQ", City = "nowhere" }],
            Users = [new() { ID = 3, Username = "x", Password = "p", CompanyBranch = "hq" }, new() { ID = 3, Username = "y", Password = "p", Teams = ["ghost"] }],
        };

        var refused = await Assert.ThrowsAsync<IdentitySeedException>(() => new IdentitySeeder(db, []).SeedAsync(seed, Ct));

        Assert.Contains(refused.Problems, x => x.Contains("Countries[1] (Again)") && x.Contains("'iq'"));
        Assert.Contains(refused.Problems, x => x.Contains("cycle") && x.Contains("a → b → a"));
        Assert.Contains(refused.Problems, x => x.Contains("CompanyBranches[0] (HQ): Company is required"));
        Assert.Contains(refused.Problems, x => x.Contains("City 'nowhere'"));
        Assert.Contains(refused.Problems, x => x.Contains("Users[1] (y)") && x.Contains("ID 3"));
        Assert.Contains(refused.Problems, x => x.Contains("Teams 'ghost'"));
        Assert.Contains(refused.Problems, x => x.Contains("Users[1] (y): CompanyBranch"));
        Assert.Empty(await db.Countries.ToListAsync(Ct));
    }

    [Fact]
    public async Task A_JSON_seed_with_an_unknown_property_is_refused_before_anything_is_written()
    {
        using var host = HostOn(() => Context());

        var refused = await Assert.ThrowsAsync<IdentitySeedException>(() =>
            host.SeedShiftIdentitySqliteAsync("""{ "countries": [{ "name": "Iraq", "callingCode": "+964", "colour": "green" }] }""", Ct));

        Assert.Contains("colour", refused.Message);
        await using var db = Context();
        Assert.Empty(await db.Countries.ToListAsync(Ct));
    }

    [Fact]
    public async Task A_new_user_without_a_password_rolls_the_whole_seed_back()
    {
        await using var db = Context();
        var seed = new IdentitySeedData
        {
            Countries = [new() { Name = "Iraq", CallingCode = "+964" }],
            Regions = [new() { Key = "r", Name = "R" }],
            Cities = [new() { Key = "c", Name = "C", Region = "r" }],
            Companies = [new() { Key = "co", Name = "Co" }],
            CompanyBranches = [new() { Key = "b", Name = "B", Company = "co", City = "c" }],
            Users = [new() { Username = "nopassword", CompanyBranch = "b" }],
        };

        var refused = await Assert.ThrowsAsync<IdentitySeedException>(() => new IdentitySeeder(db, []).SeedAsync(seed, Ct));

        Assert.Contains("nopassword", refused.Message);
        Assert.Empty(await db.Countries.ToListAsync(Ct));   // written before the user, rolled back with it
    }

    [Fact]
    public async Task A_database_without_the_identity_tables_is_refused_with_a_message_that_says_to_create_them()
    {
        await using var empty = new SqliteConnection("Data Source=:memory:");
        await empty.OpenAsync(Ct);
        await using var db = Context(empty);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => new IdentitySeeder(db, []).SeedAsync(new IdentitySeedData(), Ct));

        Assert.Contains("EnsureCreated", refused.Message);
        Assert.Contains("Users", refused.Message);
    }
}

/// <summary>A seed on the identity host's own database: the authority's startup and the v2 login see the seeded rows.</summary>
[Trait("Category", "Http")]
public sealed class SqliteSeededHostTests(SqliteIdentityFixture fixture) : IClassFixture<SqliteIdentityFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_seeded_client_app_is_not_added_again_by_the_authority_startup_and_a_seeded_user_signs_in()
    {
        const string username = "seeded-user";
        await using (var db = fixture.CreateContext())
            await new IdentitySeeder(db, [typeof(ShiftIdentityActions)]).SeedAsync(new IdentitySeedData
            {
                Countries = [new() { Key = "country", Name = "Seeded Country", CallingCode = "+1" }],
                Regions = [new() { Key = "region", Name = "Seeded Region", Country = "country" }],
                Cities = [new() { Key = "city", Name = "Seeded City", Region = "region" }],
                Companies = [new() { Key = "c", Name = "Seeded Company" }],
                CompanyBranches = [new() { Key = "b", Name = "Seeded Branch", Company = "c", City = "city" }],
                Apps = [new() { AppId = ConfiguredIdentityHttpHost<IdentityTestDbContext>.ClientId, DisplayName = "Seeded client", RedirectUri = "https://seeded.invalid/callback" }],
                Users = [new() { Username = username, Password = SqlIdentityFixture.PolicyValidPassword, CompanyBranch = "b" }],
            }, Ct);

        using var host = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture);
        await IdentityHttpHost.SignInAsync(host.Client, username, SqlIdentityFixture.PolicyValidPassword);

        await using var check = fixture.CreateContext();
        var app = await check.Apps.SingleAsync(x => x.AppId == ConfiguredIdentityHttpHost<IdentityTestDbContext>.ClientId, Ct);
        Assert.Equal("Seeded client", app.DisplayName);
    }
}

/// <summary>Collects log entries, to read what a call logged.</summary>
internal sealed class LogSink : ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Logger(this);
    public void Dispose() { }

    private sealed class Logger(LogSink sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Entries.Enqueue((logLevel, formatter(state, exception)));
    }
}
