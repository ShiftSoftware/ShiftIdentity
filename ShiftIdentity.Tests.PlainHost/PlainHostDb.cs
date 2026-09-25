using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftIdentity.Data;

namespace ShiftIdentity.Tests.PlainHost;

/// <summary>
/// The DbContext a host writes, and the only type in this project. The project file says why the project exists.
/// </summary>
public class PlainHostDb : ShiftIdentityDbContext
{
    public PlainHostDb(DbContextOptions<PlainHostDb> options) : base(options)
    {
    }
}
