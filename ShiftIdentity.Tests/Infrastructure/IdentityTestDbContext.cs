using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Authentication;

namespace ShiftIdentity.Tests.Infrastructure;

public class IdentityTestDbContext(DbContextOptions options) : ShiftIdentityDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ConfigureIdentitySecurity();
    }
}

public sealed class LegacyIdentityTestDbContext(DbContextOptions options) : ShiftIdentityDbContext(options);
