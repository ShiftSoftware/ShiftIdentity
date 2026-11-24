using ShiftSoftware.ShiftEntity.EFCore;

namespace ShiftSoftware.ShiftIdentity.Data;

/// <summary>
/// On a database other than SQL Server, sets a new row version on every row a save adds or modifies, where SQL Server
/// would generate one: on the identity's row versions that <see cref="OtherDatabases.AdaptModel"/> hands to the
/// context. The original value stays the concurrency check, so a write over a row that changed since it was read is
/// still refused.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ShiftIdentityDbContext"/> declares it only when its database is not SQL Server, after every other
/// before-save handler of a round. A row that another handler adds or changes is offered in the next round, so it gets
/// its row version too.
/// </para>
/// <para>
/// A guard: <c>SuppressAllSaveHandlers()</c> and <c>SuppressSaveHandlers(stages)</c> leave it on. It is internal, so no
/// app can name it to switch it off.
/// </para>
/// </remarks>
[Guard]
internal sealed class OtherDatabaseRowVersions : IBeforeSaveHandler<object>
{
    public ValueTask BeforeSaveAsync(PendingRows<object> rows, CancellationToken cancellationToken)
    {
        var db = rows.Db;

        foreach (var row in rows)
        {
            if (row.Change == RowChange.Deleted)
                continue;

            // A row of a shared-type entity, such as the join row of a many-to-many, has no row version, and EF cannot
            // find its entry from the instance alone.
            if (db.Model.IsShared(row.Entity.GetType()))
                continue;

            OtherDatabases.SetRowVersions(db.Entry(row.Entity));
        }

        return ValueTask.CompletedTask;
    }
}
