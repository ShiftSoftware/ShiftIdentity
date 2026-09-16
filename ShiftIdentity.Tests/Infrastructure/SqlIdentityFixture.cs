using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using Xunit;

namespace ShiftIdentity.Tests.Infrastructure;

/// <summary>
/// One owned database per fixture. Never reads appsettings, user secrets or the configured test DB.
/// Teardown checks both the generated name and a private ownership marker.
/// </summary>
public class SqlIdentityFixture : IAsyncLifetime
{
    private readonly string name = "ShiftIdentityTests_" + Guid.NewGuid().ToString("N");
    private readonly Guid ownership = Guid.NewGuid();
    private string connectionString = "";
    private string serverConnectionString = "";
    public byte[] FactorSecret { get; } = RandomNumberGenerator.GetBytes(20);
    public string LegacyRefreshKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    public int LegacyRefreshLifetimeSeconds { get; set; } = 1800;
    public bool SeedLegacyFactorBeforeExpansion { get; init; }
    public ShiftSoftware.ShiftIdentity.Core.Models.FactorProtectionSettings FactorProtection { get; } = new()
    {
        ActiveKeyId = "fixture-key",
        Keys = new() { ["fixture-key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }
    };
    internal IdentityMaterialProtector Protection => new(FactorProtection);
    public TimeProvider Clock { get; set; } = TimeProvider.System;
    public ISecurityEmailSink? EmailSink { get; set; } = new LocalSecurityInbox();
    /// <summary>
    /// The test budgets. Every ordinary public request sleeps until the sum of the three has elapsed (the response
    /// floor), so the sender wait stays short: the local inbox answers synchronously, and the padding is dropped.
    /// The result-persistence budget is the production default, because the audit or cancellation it bounds is written
    /// by a SQL transaction whose duration a loaded two-core agent decides, not the test; 100 ms was exceeded there.
    /// </summary>
    internal static SecurityDeliveryLimits TestDeliveryLimits => new(HandoffTimeoutMilliseconds: 100, ResultPersistenceTimeoutMilliseconds: 2000, PublicPaddingMilliseconds: 0);
    /// <summary>
    /// The shortest response floor, for a test that issues many public requests and reads only the inbox, the
    /// delivery counters or the response body. Such a test never observes the handoff result being persisted, so
    /// that budget may expire without changing what it asserts.
    /// </summary>
    internal static SecurityDeliveryLimits FastPublicResponses => new(HandoffTimeoutMilliseconds: 100, ResultPersistenceTimeoutMilliseconds: 100, PublicPaddingMilliseconds: 0);
    internal SecurityDeliveryLimits DeliveryLimits { get; set; } = TestDeliveryLimits;
    public void UseRuntimeDeliveryLimits() => DeliveryLimits = new();
    internal void UseFastPublicResponses() => DeliveryLimits = FastPublicResponses;
    public Func<DbContextOptions, ShiftIdentityDbContext>? ContextFactory { get; set; }
    internal IdentityAdmissionOptions Options { get; private set; } = null!;
    public string Password { get; } = "Synthetic Password 7!";
    public long UserID { get; private set; }
    public string Username { get; } = "synthetic-" + Guid.NewGuid().ToString("N");

    public async ValueTask InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("SHIFT_IDENTITY_TEST_SQL");
        var builder = new SqlConnectionStringBuilder(configured ??
            @"Server=.\SQLEXPRESS;Integrated Security=true;Encrypt=true;TrustServerCertificate=true;Connect Timeout=5");
        // A test override supplies local/container connection details, never a remote application DB.
        if (!Regex.IsMatch(builder.DataSource, @"^(?:\.\\SQLEXPRESS|localhost(?:,\d+)?|127\.0\.0\.1(?:,\d+)?)$", RegexOptions.IgnoreCase))
            throw new InvalidOperationException("Identity SQL tests require a local disposable SQL Server.");
        if (!string.IsNullOrEmpty(builder.InitialCatalog) && builder.InitialCatalog != "master")
            throw new InvalidOperationException("Do not supply an existing database to identity SQL tests.");
        builder.InitialCatalog = "master";
        serverConnectionString = builder.ConnectionString;
        await using (var server = new SqlConnection(serverConnectionString))
        {
            await server.OpenAsync();
            await using var create = server.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{name}]";
            await create.ExecuteNonQueryAsync();
        }
        builder.InitialCatalog = name;
        connectionString = builder.ConnectionString;
        await using (var owned = new SqlConnection(connectionString))
        {
            await owned.OpenAsync();
            await using var mark = owned.CreateCommand();
            mark.CommandText = "CREATE TABLE [dbo].[IdentityTestOwnership] ([Marker] uniqueidentifier NOT NULL PRIMARY KEY); INSERT INTO [dbo].[IdentityTestOwnership] VALUES (@marker)";
            mark.Parameters.AddWithValue("@marker", ownership);
            await mark.ExecuteNonQueryAsync();
        }
        using var rsa = RSA.Create(2048);
        Options = new("https://identity.invalid", "identity-refresh", rsa.ExportRSAPrivateKey(),
            RandomNumberGenerator.GetBytes(64), RandomNumberGenerator.GetBytes(32));
        await using var legacy = new LegacyIdentityTestDbContext(BuildOptions());
        await legacy.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        var hash = HashService.GenerateHash(Password);
        var country = new Country { Name = "Synthetic Country", CallingCode = "+1" };
        var region = new Region { Name = "Synthetic Region", Country = country };
        var city = new City { Name = "Synthetic City", Region = region };
        var company = new Company { Name = "Synthetic Company" };
        var branch = new CompanyBranch { Name = "Synthetic Branch", Company = company, Region = region, City = city };
        legacy.Add(branch);
        await legacy.SaveChangesAsync();
        var user = new User { Username = Username, FullName = "Synthetic User", IsActive = true,
            PasswordHash = hash.PasswordHash, Salt = hash.Salt, RegionID = region.ID, CountryID = country.ID,
            CompanyID = company.ID, CompanyBranchID = branch.ID,
            TotpSecret = SeedLegacyFactorBeforeExpansion ? FactorSecret : null };
        legacy.Users.Add(user);
        legacy.Apps.AddRange(new App { AppId = "test-client", DisplayName = "Test", RedirectUri = "https://client.invalid/callback" },
            new App { AppId = "other-client", DisplayName = "Other", RedirectUri = "https://other.invalid/callback" });
        await legacy.SaveChangesAsync();
        UserID = user.ID;
        // Exercise an actual relational migration from the existing identity model to the staged model.
        await using var current = CreateContext();
        var oldModel = legacy.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var newModel = current.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var operations = current.GetService<IMigrationsModelDiffer>().GetDifferences(oldModel, newModel);
        var commands = current.GetService<IMigrationsSqlGenerator>().Generate(operations, current.GetService<IDesignTimeModel>().Model);
        foreach (var command in commands)
            await current.Database.ExecuteSqlRawAsync(command.CommandText);
        await current.Database.ExecuteSqlRawAsync(
            "INSERT INTO [ShiftIdentity].[UserSecurityStates] ([UserID],[SecurityVersion],[FactorGeneration],[LocalMfaRecoveryRequired],[FailedProofs]) SELECT [ID],1,1,0,0 FROM [ShiftIdentity].[Users]");
        current.Set<AuthenticationPolicyState>().Add(new());
        RecoveryContact.InitializeLookup(user, await current.Set<UserSecurityState>().SingleAsync(x => x.UserID == UserID));
        await current.SaveChangesAsync();
    }

    public DbContextOptions BuildOptions(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors) =>
        new DbContextOptionsBuilder().UseSqlServer(connectionString).AddInterceptors(interceptors).Options;

    public ShiftIdentityDbContext CreateContext(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors) =>
        ContextFactory?.Invoke(BuildOptions(interceptors)) ?? new IdentityTestDbContext(BuildOptions(interceptors));

    public ShiftIdentityDbContext CreateContext(IServiceProvider services, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder(BuildOptions(interceptors)).UseApplicationServiceProvider(services).Options;
        return ContextFactory?.Invoke(options) ?? new IdentityTestDbContext(options);
    }

    public async Task<long> CreateSyntheticUserAsync(string username, string? accessTree = null, bool mfa = false, string? email = null)
    {
        await using var db = CreateContext();
        var template = await db.Users.AsNoTracking().SingleAsync(x => x.ID == UserID);
        var hash = HashService.GenerateVersionedHash(Password);
        var user = new User
        {
            Username = username, Email = email, FullName = "Synthetic User", IsActive = true, PasswordHash = hash.PasswordHash, Salt = hash.Salt,
            CompanyID = template.CompanyID, CompanyBranchID = template.CompanyBranchID, CountryID = template.CountryID,
            RegionID = template.RegionID, AccessTree = accessTree
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var security = new UserSecurityState { UserID = user.ID };
        SetSyntheticFactor(security, mfa ? FactorSecret : null);
        RecoveryContact.InitializeLookup(user, security);
        db.Set<UserSecurityState>().Add(security);
        await db.SaveChangesAsync();
        return user.ID;
    }

    public async Task ResetAsync(bool mfa = false)
    {
        await using var db = CreateContext();
        EmailSink = new LocalSecurityInbox(Clock);
        DeliveryLimits = TestDeliveryLimits;
        await db.Set<AuthThrottleBucket>().ExecuteDeleteAsync();
        await db.Set<AuthenticationOperation>().ExecuteDeleteAsync();
        await db.Set<AuthenticationAuditEvent>().ExecuteDeleteAsync();
        var user = await db.Users.SingleAsync(x => x.ID == UserID);
        var hash = HashService.GenerateHash(Password);
        user.PasswordHash = hash.PasswordHash; user.Salt = hash.Salt;
        user.IsActive = true; user.IsDeleted = false; user.RequireChangePassword = false;
        user.LockDownUntil = null; user.Email = null; user.EmailVerified = false;
        user.TotpSecret = null;
        var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == UserID);
        state.SecurityVersion = 1; state.FactorGeneration = 1; state.LocalMfaRecoveryRequired = false;
        state.ContactRevision = 1; RecoveryContact.Invalidate(state);
        state.UsernameLookupKey = null; state.EmailLookupKey = null; RecoveryContact.InitializeLookup(user, state);
        state.LastDeliveryAt = null; state.DeliveryWindowStart = null; state.DeliveryCount = 0;
        state.TotpProtectionVersion = 0; state.MfaRecoveryOperationID = null;
        state.FailedProofs = 0; state.FailureWindowStart = null; state.LastAcceptedTotpStep = null;
        SetSyntheticFactor(state, mfa ? FactorSecret : null);
        var policy = await db.Set<AuthenticationPolicyState>().SingleAsync();
        policy.Revision = 1; policy.MfaEnabled = true; policy.MfaMandatory = false; policy.RequireVerifiedEmail = false;
        policy.TotpDigits = 6; policy.TotpPeriodSeconds = 30; policy.TotpWindowPast = 1; policy.TotpWindowFuture = 1;
        await db.SaveChangesAsync();
        Options = Options with { PolicyRevision = 1 };
    }

    // Controls for the isolated consumer preview. These never read application configuration.
    public async Task ChangeMfaPolicyAsync(bool mandatory)
    {
        await using var db = CreateContext();
        var policy = await db.Set<AuthenticationPolicyState>().SingleAsync();
        policy.MfaMandatory = mandatory;
        policy.Revision = checked(policy.Revision + 1);
        await db.SaveChangesAsync();
        Options = Options with { PolicyRevision = policy.Revision };
    }

    public async Task ChangeVerifiedEmailPolicyAsync(bool required)
    {
        await using var db = CreateContext();
        var policy = await db.Set<AuthenticationPolicyState>().SingleAsync();
        policy.RequireVerifiedEmail = required;
        policy.Revision = checked(policy.Revision + 1);
        await db.SaveChangesAsync();
        Options = Options with { PolicyRevision = policy.Revision };
    }

    public async Task<(long UserID, string? Code)> GetSyntheticFactorAsync(string username,
        DateTimeOffset? generatedAt = null, CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext();
        var id = await db.Users.Where(x => x.Username == username).Select(x => x.ID).SingleAsync(cancellationToken);
        var state = await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == id, cancellationToken);
        if (state.ProtectedTotpSecret is null) return (id, null);
        var secret = ReadSyntheticFactor(state);
        try { return (id, new OtpNet.Totp(secret).ComputeTotp((generatedAt ?? Clock.GetUtcNow()).UtcDateTime)); }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    public void SetSyntheticFactor(UserSecurityState state, byte[]? secret)
    {
        if (secret is null) { state.ProtectedTotpSecret = null; state.TotpProtectionVersion = 0; }
        else MfaMaterial.ProtectActive(Protection, state, secret);
    }

    public byte[] ReadSyntheticFactor(UserSecurityState state) => MfaMaterial.ReadActive(Protection, state);

    public async ValueTask DisposeAsync()
    {
        if (string.IsNullOrEmpty(connectionString)) return;
        if (!Regex.IsMatch(name, "^ShiftIdentityTests_[a-f0-9]{32}$")) throw new InvalidOperationException("Unsafe database name.");
        await using (var owned = new SqlConnection(connectionString))
        {
            await owned.OpenAsync();
            await using var check = owned.CreateCommand();
            check.CommandText = "SELECT [Marker] FROM [dbo].[IdentityTestOwnership]";
            if (await check.ExecuteScalarAsync() is not Guid marker || marker != ownership)
                throw new InvalidOperationException("Database ownership marker mismatch; refusing teardown.");
        }
        // Only this database's pooled connections must go before the drop; clearing every pool in the process would
        // cost the classes still running against their own databases a reconnect in the middle of a request.
        SqlConnection.ClearPool(new SqlConnection(connectionString));
        await using var server = new SqlConnection(serverConnectionString);
        await server.OpenAsync();
        await using var drop = server.CreateCommand();
        drop.CommandText = $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]";
        await drop.ExecuteNonQueryAsync();
    }
}

// Classes in one collection already run one after another, sharing this fixture's database. DisableParallelization
// would not add to that; it only makes xUnit hold the whole collection back until every other collection has
// finished, which turned the run into two serial halves.
[CollectionDefinition("Identity SQL")]
public sealed class IdentitySqlCollection : ICollectionFixture<SqlIdentityFixture>;
