using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftIdentity.Tests.Infrastructure;
using Xunit;

namespace ShiftIdentity.Tests.Sqlite;

/// <summary>
/// The identity model on a database other than SQL Server: created on SQLite as it is, SQL Server's check constraints
/// and collations left out, row versions set by the context and still refusing a stale write, DateTimeOffset sorted and
/// compared by instant in queries, as on SQL Server. A SQL Server context of the same type in the same process keeps the SQL Server model.
/// </summary>
public sealed class SqliteIdentityModelTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private DbContextOptions<SqliteIdentityTestDbContext> options = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await connection.OpenAsync(Ct);
        options = new DbContextOptionsBuilder<SqliteIdentityTestDbContext>().UseSqlite(connection).Options;
        await using var db = Context();
        await db.Database.EnsureCreatedAsync(Ct);
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();

    private SqliteIdentityTestDbContext Context() => new(options);

    private static SqliteIdentityTestDbContext SqlServerContext() =>
        // Never connects: only the model is read.
        new(new DbContextOptionsBuilder<SqliteIdentityTestDbContext>().UseSqlServer("Server=unused.invalid;Database=unused").Options);

    [Fact]
    public async Task A_SQLite_and_a_SQL_Server_context_of_the_same_type_each_keep_their_own_model()
    {
        await using var sqlite = Context();
        await using var sqlServer = SqlServerContext();
        await using var sqliteAgain = Context();

        // Check constraints and collations are in the design-time model only.
        var sqliteModel = sqlite.GetService<IDesignTimeModel>().Model;
        var sqlServerModel = sqlServer.GetService<IDesignTimeModel>().Model;
        var onSqlite = sqliteModel.FindEntityType(typeof(UserSecurityState))!;
        var onSqlServer = sqlServerModel.FindEntityType(typeof(UserSecurityState))!;

        Assert.Empty(onSqlite.GetCheckConstraints());
        Assert.Null(onSqlite.FindProperty(nameof(UserSecurityState.UsernameLookupKey))!.GetCollation());
        Assert.Equal(ValueGenerated.Never, onSqlite.FindProperty(nameof(UserSecurityState.RowVersion))!.ValueGenerated);
        Assert.True(onSqlite.FindProperty(nameof(UserSecurityState.RowVersion))!.IsConcurrencyToken);
        Assert.Equal(typeof(long), sqliteModel.FindEntityType(typeof(User))!.FindProperty(nameof(User.LastSaveDate))!.GetValueConverter()?.ProviderClrType);

        Assert.NotEmpty(onSqlServer.GetCheckConstraints());
        Assert.Equal("Latin1_General_100_BIN2", onSqlServer.FindProperty(nameof(UserSecurityState.UsernameLookupKey))!.GetCollation());
        Assert.Equal(ValueGenerated.OnAddOrUpdate, onSqlServer.FindProperty(nameof(UserSecurityState.RowVersion))!.ValueGenerated);
        Assert.Null(sqlServerModel.FindEntityType(typeof(User))!.FindProperty(nameof(User.LastSaveDate))!.GetValueConverter());

        Assert.Same(sqlite.Model, sqliteAgain.Model);
        Assert.NotSame(sqlite.Model, sqlServer.Model);
    }

    [Fact]
    public async Task Every_insert_and_update_sets_a_new_row_version()
    {
        await using var db = Context();
        var policy = new AuthenticationPolicyState();
        var bucket = new AuthThrottleBucket { Key = "key", WindowStart = DateTimeOffset.UtcNow };
        db.AddRange(policy, bucket);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(8, policy.RowVersion.Length);
        Assert.Equal(8, bucket.RowVersion.Length);
        var (policyVersion, bucketVersion) = (policy.RowVersion, bucket.RowVersion);

        policy.MfaMandatory = true;
        await db.SaveChangesAsync(Ct);

        Assert.NotEqual(policyVersion, policy.RowVersion);
        Assert.Equal(bucketVersion, bucket.RowVersion);   // not written, not changed
    }

    [Fact]
    public async Task A_save_over_a_row_changed_since_it_was_read_is_refused()
    {
        await using (var seed = Context())
        {
            seed.Add(new AuthenticationPolicyState());
            await seed.SaveChangesAsync(Ct);
        }
        await using var first = Context();
        await using var second = Context();
        var firstRead = await first.Set<AuthenticationPolicyState>().SingleAsync(Ct);
        var secondRead = await second.Set<AuthenticationPolicyState>().SingleAsync(Ct);

        firstRead.Revision++;
        await first.SaveChangesAsync(Ct);
        secondRead.Revision++;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Ct));
    }

    private static DeviceAuthorization Device(DateTimeOffset expiresAt) => new()
    {
        ID = Guid.NewGuid(), DeviceCodeDigest = Guid.NewGuid().ToByteArray(), UserCodeDigest = Guid.NewGuid().ToByteArray(),
        ClientID = "tv", Audience = "audience", State = DeviceAuthorizationState.Pending,
        CreatedAt = expiresAt.AddMinutes(-10), ExpiresAt = expiresAt, Interval = 5,
    };

    [Fact]
    public async Task DateTimeOffset_values_sort_and_compare_by_instant_in_a_query()
    {
        // 10:00 at +03:00 is 07:00 UTC, before 08:00 at +00:00.
        var day = new DateTime(2026, 1, 1);
        var early = Device(new DateTimeOffset(day.AddHours(10), TimeSpan.FromHours(3)));
        var late = Device(new DateTimeOffset(day.AddHours(8), TimeSpan.Zero));
        await using var db = Context();
        db.AddRange(late, early);
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();

        var ordered = await db.Set<DeviceAuthorization>().OrderBy(x => x.ExpiresAt).Select(x => x.ID).ToListAsync(Ct);
        var cutoff = new DateTimeOffset(day.AddHours(7.5), TimeSpan.Zero);
        var before = await db.Set<DeviceAuthorization>().Where(x => x.ExpiresAt < cutoff).Select(x => x.ID).ToListAsync(Ct);

        Assert.Equal([early.ID, late.ID], ordered);
        Assert.Equal([early.ID], before);
        Assert.Equal(early.ExpiresAt, (await db.Set<DeviceAuthorization>().SingleAsync(x => x.ID == early.ID, Ct)).ExpiresAt);
    }

    [Fact]
    public async Task The_device_expiry_bulk_update_refuses_a_save_from_a_read_made_before_it()
    {
        var device = Device(DateTimeOffset.UtcNow.AddMinutes(-1));
        await using (var seed = Context())
        {
            seed.Add(device);
            await seed.SaveChangesAsync(Ct);
        }
        await using var approver = Context();
        var stale = await approver.Set<DeviceAuthorization>().SingleAsync(x => x.ID == device.ID, Ct);

        await using (var maintenance = Context())
            await new SqlIdentitySecurityStore(maintenance).CleanupDeviceAuthorizationsAsync(DateTimeOffset.UtcNow, Ct);

        stale.State = DeviceAuthorizationState.Approved;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => approver.SaveChangesAsync(Ct));
        await using var check = Context();
        Assert.Equal(DeviceAuthorizationState.Expired, (await check.Set<DeviceAuthorization>().SingleAsync(x => x.ID == device.ID, Ct)).State);
    }

    [Fact]
    public async Task A_company_calendar_with_its_groups_is_saved_and_read_back()
    {
        await using (var db = Context())
        {
            db.CompanyCalendars.Add(new CompanyCalendar
            {
                Title = "Ramadan", StartDate = new DateTime(2026, 2, 18), EndDate = new DateTime(2026, 3, 19), Priority = 1,
                ShiftGroups = [new() { DepartmentIds = [1, 2], Days = [0, 1] }],
                WeekendGroups = [new() { BrandIds = [3] }],
            });
            await db.SaveChangesAsync(Ct);
        }

        await using var read = Context();
        var calendar = await read.CompanyCalendars.SingleAsync(Ct);
        Assert.Equal([1L, 2L], calendar.ShiftGroups.Single().DepartmentIds);
        Assert.Equal([3L], calendar.WeekendGroups.Single().BrandIds);
    }

    [Fact]
    public async Task On_SQLite_a_table_in_a_schema_is_named_with_it_so_a_consumer_table_of_the_same_name_fits_beside_it()
    {
        await using var other = new SqliteConnection("Data Source=:memory:");
        await other.OpenAsync(Ct);
        await using var db = new ConsumerWithBrands(new DbContextOptionsBuilder<ConsumerWithBrands>().UseSqlite(other).Options);
        await db.Database.EnsureCreatedAsync(Ct);

        db.Add(new ConsumerBrand { Name = "Consumer" });
        db.Brands.Add(new Brand { Name = "Identity" });
        await db.SaveChangesAsync(Ct);

        Assert.Equal("Brands", db.Model.FindEntityType(typeof(ConsumerBrand))!.GetTableName());
        Assert.Equal("ShiftIdentity_Brands", db.Model.FindEntityType(typeof(Brand))!.GetTableName());
        Assert.Null(db.Model.FindEntityType(typeof(Brand))!.GetSchema());
        await using var sqlServer = SqlServerContext();
        Assert.Equal(("Brands", "ShiftIdentity"), (sqlServer.Model.FindEntityType(typeof(Brand))!.GetTableName(), sqlServer.Model.FindEntityType(typeof(Brand))!.GetSchema()));
    }

    // A consumer with a Brands table of its own beside the identity's, as the StockPlusPlus sample has.
    private sealed class ConsumerBrand
    {
        public long ID { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class ConsumerWithBrands(DbContextOptions<ConsumerWithBrands> options) : ShiftIdentityDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<ConsumerBrand>().ToTable("Brands");
            base.OnModelCreating(builder);
        }
    }
}
