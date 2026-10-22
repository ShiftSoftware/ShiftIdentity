using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftIdentity.Tests.Infrastructure;

/// <summary>The current identity model; the base context carries the authority's schema.</summary>
public class IdentityTestDbContext(DbContextOptions options) : ShiftIdentityDbContext(options);

/// <summary>
/// The identity model as a host had it before the authority's schema existed. The fixture creates a database from
/// this model and then applies the relational diff to the current one, the expansion a host's next migration carries.
/// </summary>
public class LegacyIdentityTestDbContext(DbContextOptions options) : ShiftIdentityDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Ignore<UserSecurityState>();
        builder.Ignore<AuthenticationOperation>();
        builder.Ignore<AuthenticationPolicyState>();
        builder.Ignore<AuthenticationAuditEvent>();
        builder.Ignore<AuthThrottleBucket>();
        builder.Ignore<DeviceAuthorization>();
    }
}

// EF caches one model per context type, so a fixture that maps the identity entities as temporal tables (the
// template's UseTemporal) needs context types of its own; the option alone would reuse the non-temporal model.
public sealed class TemporalIdentityTestDbContext(DbContextOptions options) : IdentityTestDbContext(options);
public sealed class TemporalLegacyIdentityTestDbContext(DbContextOptions options) : LegacyIdentityTestDbContext(options);

/// <summary>
/// The identity model on SQLite, for fast tests that need real tables. Left out: the tables SQLite cannot create, which
/// these tests do not read. The calendar has nvarchar(max) columns; the security tables have SQL Server check
/// constraints and collations.
/// </summary>
public sealed class SqliteIdentityTestDbContext(DbContextOptions<SqliteIdentityTestDbContext> options) : ShiftIdentityDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Ignore<CompanyCalendarBranch>();
        builder.Ignore<CompanyCalendar>();
        builder.Ignore<UserSecurityState>();
        builder.Ignore<AuthenticationOperation>();
        builder.Ignore<UserProviderLink>();
        builder.Ignore<AuthenticationPolicyState>();
        builder.Ignore<AuthenticationAuditEvent>();
        builder.Ignore<AuthThrottleBucket>();
        builder.Ignore<DeviceAuthorization>();
    }
}
