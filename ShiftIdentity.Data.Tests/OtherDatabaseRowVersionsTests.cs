using System.Collections;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Data.Tests;

/// <summary>
/// The identity's row versions on a database other than SQL Server, which the context sets itself with a before-save
/// guard: a row that another handler adds or changes gets one, the blanket switches leave the guard on, a write over a
/// row that changed since it was read is still refused, a host's own rows are left alone, and on SQL Server the guard is
/// not declared at all. On SQLite, except where SQL Server's model is read.
/// </summary>
public sealed class OtherDatabaseRowVersionsTests : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    // ── A host's context ────────────────────────────────────────────────────────

    /// <summary>A host's own record, with a concurrency token that the host sets itself.</summary>
    public sealed class Widget
    {
        public int ID { get; set; }
        public byte[] Version { get; set; } = [];
    }

    /// <summary>Two host records in a many-to-many: their join rows are of a shared-type entity.</summary>
    public sealed class Note
    {
        public int ID { get; set; }
        public List<Label> Labels { get; set; } = [];
    }

    public sealed class Label
    {
        public int ID { get; set; }
        public List<Note> Notes { get; set; } = [];
    }

    /// <summary>A host's handler that adds a throttle bucket for each audit event added with its outcome.</summary>
    public sealed class AddsABucket : IBeforeSaveHandler<AuthenticationAuditEvent>
    {
        public const string Outcome = "adds a bucket";

        public ValueTask BeforeSaveAsync(PendingRows<AuthenticationAuditEvent> rows, CancellationToken cancellationToken)
        {
            foreach (var row in rows)
                if (row.Change == RowChange.Added && row.Entity.Outcome == Outcome)
                    rows.Db.Add(new AuthThrottleBucket { Key = "added by a handler", WindowStart = row.Entity.CreatedAt });

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A host's handler that counts one more in every bucket the context holds, for each audit event added with its outcome.</summary>
    public sealed class CountsTheBuckets : IBeforeSaveHandler<AuthenticationAuditEvent>
    {
        public const string Outcome = "counts the buckets";

        public ValueTask BeforeSaveAsync(PendingRows<AuthenticationAuditEvent> rows, CancellationToken cancellationToken)
        {
            foreach (var row in rows)
                if (row.Change == RowChange.Added && row.Entity.Outcome == Outcome)
                    foreach (var bucket in rows.Db.Set<AuthThrottleBucket>().Local)
                        bucket.Count++;

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A host's context: the identity model with records of the host's own, and two handlers of the host's.</summary>
    private sealed class HostDb(DbContextOptions<HostDb> options) : ShiftIdentityDbContext(options)
    {
        public DbSet<Widget> Widgets => Set<Widget>();
        public DbSet<Note> Notes => Set<Note>();
        public DbSet<Label> Labels => Set<Label>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<Widget>().Property(x => x.Version).IsConcurrencyToken();
        }

        protected override void ConfigureSaveHandlers(SaveHandlerSet handlers)
        {
            base.ConfigureSaveHandlers(handlers);
            handlers.Add<AddsABucket>().Add<CountsTheBuckets>();
        }
    }

    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private DbContextOptions<HostDb> options = null!;

    public async ValueTask InitializeAsync()
    {
        await connection.OpenAsync(Ct);
        options = new DbContextOptionsBuilder<HostDb>().UseSqlite(connection).Options;
        await using var db = Context();
        await db.Database.EnsureCreatedAsync(Ct);
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();

    private HostDb Context() => new(options);

    // Never connects: only the model and the handlers it declares are read.
    private static HostDb OnSqlServer() =>
        new(new DbContextOptionsBuilder<HostDb>().UseSqlServer("Server=unused.invalid;Database=unused").Options);

    private async Task<AuthThrottleBucket> SeedABucketAsync()
    {
        await using var db = Context();
        var bucket = new AuthThrottleBucket { Key = "seeded", WindowStart = Now };
        db.Add(bucket);
        await db.SaveChangesAsync(Ct);
        return bucket;
    }

    // ── Rows that other handlers add or change ──────────────────────────────────

    [Fact]
    public async Task A_row_that_another_before_save_handler_adds_gets_a_row_version()
    {
        await using var db = Context();
        db.Add(new AuthenticationAuditEvent { Outcome = AddsABucket.Outcome, CreatedAt = Now });

        await db.SaveChangesAsync(Ct);

        var added = Assert.Single(db.Set<AuthThrottleBucket>().Local);
        Assert.Equal(8, added.RowVersion.Length);

        await using var read = Context();
        Assert.Equal(added.RowVersion, (await read.Set<AuthThrottleBucket>().SingleAsync(Ct)).RowVersion);
    }

    [Fact]
    public async Task A_row_that_another_before_save_handler_changes_gets_a_new_row_version()
    {
        var seeded = await SeedABucketAsync();

        await using var db = Context();
        var bucket = await db.Set<AuthThrottleBucket>().SingleAsync(Ct);
        db.Add(new AuthenticationAuditEvent { Outcome = CountsTheBuckets.Outcome, CreatedAt = Now });

        await db.SaveChangesAsync(Ct);

        Assert.Equal(1, bucket.Count);
        Assert.Equal(8, bucket.RowVersion.Length);
        Assert.NotEqual(seeded.RowVersion, bucket.RowVersion);

        await using var read = Context();
        var stored = await read.Set<AuthThrottleBucket>().SingleAsync(Ct);
        Assert.Equal(1, stored.Count);
        Assert.Equal(bucket.RowVersion, stored.RowVersion);
    }

    // ── Switches ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_save_with_every_handler_switched_off_still_sets_the_row_versions_and_only_naming_the_guard_stops_it()
    {
        await using var db = Context();
        var bucket = new AuthThrottleBucket { Key = "switched off", WindowStart = Now };
        db.Add(bucket);
        db.Add(new AuthenticationAuditEvent { Outcome = AddsABucket.Outcome, CreatedAt = Now });

        using (db.SuppressAllSaveHandlers())
            await db.SaveChangesAsync(Ct);

        // The host's handler was switched off, and the guard was not.
        Assert.Same(bucket, Assert.Single(db.Set<AuthThrottleBucket>().Local));
        Assert.Equal(8, bucket.RowVersion.Length);

        var first = bucket.RowVersion;
        bucket.Count = 1;

        using (db.SuppressSaveHandlers(SaveStages.BeforeSave))
            await db.SaveChangesAsync(Ct);

        Assert.Equal(8, bucket.RowVersion.Length);
        Assert.NotEqual(first, bucket.RowVersion);

        // Naming the guard is the one way past it, and only the identity's own code can name it: it is internal.
        var second = bucket.RowVersion;
        bucket.Count = 2;

        using (db.SuppressSaveHandler<OtherDatabaseRowVersions>())
            await db.SaveChangesAsync(Ct);

        Assert.Equal(second, bucket.RowVersion);
    }

    // ── A write over a row that changed since it was read ───────────────────────

    [Fact]
    public async Task A_write_over_a_row_that_a_handler_changed_since_it_was_read_is_refused()
    {
        await SeedABucketAsync();

        await using var updating = Context();
        var toUpdate = await updating.Set<AuthThrottleBucket>().SingleAsync(Ct);

        await using var deleting = Context();
        var toDelete = await deleting.Set<AuthThrottleBucket>().SingleAsync(Ct);

        // After both reads, another save changes the bucket through a handler.
        await using (var db = Context())
        {
            await db.Set<AuthThrottleBucket>().LoadAsync(Ct);
            db.Add(new AuthenticationAuditEvent { Outcome = CountsTheBuckets.Outcome, CreatedAt = Now });
            await db.SaveChangesAsync(Ct);
        }

        toUpdate.Count = 10;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => updating.SaveChangesAsync(Ct));

        deleting.Remove(toDelete);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => deleting.SaveChangesAsync(Ct));

        await using var read = Context();
        Assert.Equal(1, (await read.Set<AuthThrottleBucket>().SingleAsync(Ct)).Count);
    }

    // ── What the guard leaves alone ─────────────────────────────────────────────

    [Fact]
    public async Task A_hosts_own_rows_are_left_alone_its_own_concurrency_token_and_the_join_rows_of_a_many_to_many()
    {
        await using (var db = Context())
        {
            var widget = new Widget { Version = [1, 2, 3] };
            db.AddRange(widget, new Note { Labels = [new Label(), new Label()] });
            await db.SaveChangesAsync(Ct);

            Assert.Equal(new byte[] { 1, 2, 3 }, widget.Version);

            widget.Version = [4, 5, 6];
            await db.SaveChangesAsync(Ct);
        }

        await using var read = Context();
        Assert.Equal(new byte[] { 4, 5, 6 }, (await read.Widgets.SingleAsync(Ct)).Version);
        Assert.Equal(2, await read.Notes.SelectMany(note => note.Labels).CountAsync(Ct));
    }

    [Fact]
    public async Task The_guard_finds_exactly_the_row_versions_the_model_hands_to_the_context_every_identity_row_version_and_nothing_else()
    {
        var identity = typeof(ShiftIdentityDbContext).Assembly;

        // On SQL Server: the identity's row versions, which the server generates. Not the host's concurrency token.
        await using var sqlServer = OnSqlServer();
        var rowVersions = Properties(sqlServer.Model, property => property.DeclaringType.ClrType.Assembly == identity &&
            property.ClrType == typeof(byte[]) && property.IsConcurrencyToken && property.ValueGenerated == ValueGenerated.OnAddOrUpdate);

        // On SQLite: the properties the guard sets, in the model the context adapted. Should the model stop marking a row
        // version the way the guard reads it, the guard finds none of them and this fails.
        await using var sqlite = Context();
        var found = Properties(sqlite.Model, OtherDatabases.IsSetByTheContext);

        Assert.NotEmpty(rowVersions);
        Assert.Equal(rowVersions, found);

        // SQL Server's model hands the context nothing to set.
        Assert.Empty(Properties(sqlServer.Model, OtherDatabases.IsSetByTheContext));
    }

    private static List<string> Properties(IModel model, Func<IProperty, bool> which) =>
        model.GetEntityTypes()
            .SelectMany(type => type.GetDeclaredProperties())
            .Where(which)
            .Select(property => $"{property.DeclaringType.ClrType.Name}.{property.Name}")
            .Order(StringComparer.Ordinal)
            .ToList();

    // ── Declared only off SQL Server ────────────────────────────────────────────

    [Fact]
    public async Task On_SQL_Server_the_identity_context_declares_no_row_version_guard()
    {
        await using var sqlServer = OnSqlServer();
        await using var sqlite = Context();

        var onSqlServer = Declared(sqlServer);
        var onSqlite = Declared(sqlite);

        Assert.DoesNotContain(onSqlServer, declared => declared.Handler == typeof(OtherDatabaseRowVersions));
        Assert.Contains((typeof(OtherDatabaseRowVersions), int.MaxValue), onSqlite);

        // Everything else is declared alike on both. The handlers are worked out once for each model, and each context
        // has a model of its own.
        Assert.Contains((typeof(FeatureLock), int.MinValue), onSqlServer);
        Assert.Equal(onSqlServer, onSqlite.Where(declared => declared.Handler != typeof(OtherDatabaseRowVersions)));
        Assert.NotSame(sqlServer.Model, sqlite.Model);
    }

    /// <summary>
    /// What a context declares in <c>ConfigureSaveHandlers</c>, in the order it declares them, read through a set of the
    /// framework's own, as the framework reads it. The set's list of declarations is internal, so it is read by name.
    /// </summary>
    private static List<(Type Handler, int Order)> Declared(ShiftDbContext context)
    {
        var set = (SaveHandlerSet)Activator.CreateInstance(typeof(SaveHandlerSet), nonPublic: true)!;

        typeof(ShiftDbContext).GetMethod("ConfigureSaveHandlers", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(context, [set]);

        var declared = (IEnumerable)typeof(SaveHandlerSet).GetProperty("Declared", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(set)!;

        return declared.Cast<object>()
            .Select(registration => (
                (Type)registration.GetType().GetProperty("HandlerType")!.GetValue(registration)!,
                (int)registration.GetType().GetProperty("Order")!.GetValue(registration)!))
            .ToList();
    }
}
