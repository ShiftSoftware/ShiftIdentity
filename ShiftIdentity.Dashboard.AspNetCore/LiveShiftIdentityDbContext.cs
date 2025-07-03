using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftIdentity.Data;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore;

public class LiveShiftIdentityDbContext : ShiftIdentityDbContext
{
    public LiveShiftIdentityDbContext(DbContextOptions<LiveShiftIdentityDbContext> options)
            : base(options)
    {
    }
}