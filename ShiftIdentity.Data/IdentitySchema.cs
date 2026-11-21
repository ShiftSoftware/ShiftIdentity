using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace ShiftSoftware.ShiftIdentity.Data;

/// <summary>Whether the identity tables exist, for a database the host creates itself (<c>EnsureCreated</c>).</summary>
public static class IdentitySchema
{
    /// <summary>
    /// The identity tables that do not exist. Each is read with a query that returns no row, named the way the
    /// database names it. The identity's own tables only; the consumer's are not checked.
    /// </summary>
    public static async Task<IReadOnlyList<string>> FindMissingTablesAsync(ShiftIdentityDbContext db, CancellationToken ct = default)
    {
        var sql = db.GetService<ISqlGenerationHelper>();
        var identityAssembly = typeof(ShiftIdentityDbContext).Assembly;
        var tables = db.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables
            .Where(t => t.EntityTypeMappings.Any(m => m.TypeBase.ClrType.Assembly == identityAssembly))
            .OrderBy(t => t.Name, StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var table in tables)
        {
            try
            {
#pragma warning disable EF1002 // The names come from the EF model and are delimited by the provider; no value is concatenated.
                await db.Database.ExecuteSqlRawAsync($"SELECT 1 FROM {sql.DelimitIdentifier(table.Name, table.Schema)} WHERE 1 = 0", ct);
#pragma warning restore EF1002
            }
            catch (DbException) { missing.Add(table.Name); }
        }
        return missing;
    }
}
