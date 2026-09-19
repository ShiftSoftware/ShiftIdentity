using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Authentication;

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
    }
}

// EF caches one model per context type, so a fixture that maps the identity entities as temporal tables (the
// template's UseTemporal) needs context types of its own; the option alone would reuse the non-temporal model.
public sealed class TemporalIdentityTestDbContext(DbContextOptions options) : IdentityTestDbContext(options);
public sealed class TemporalLegacyIdentityTestDbContext(DbContextOptions options) : LegacyIdentityTestDbContext(options);
