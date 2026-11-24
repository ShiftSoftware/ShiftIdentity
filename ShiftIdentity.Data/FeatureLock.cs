using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.Data;

/// <summary>
/// Enforces ShiftIdentity feature locking on every repository save, WITHOUT a per-repository
/// <c>SaveChangesAsync</c> override. It is a before-save handler for the entity types the lock map names
/// (<see cref="ShiftIdentityFeatureLocking"/>), declared with the identity model, so every host that saves them has
/// it. When a repository's save is about to write a row of a feature that is currently locked, the save is refused
/// with the same localized message the old per-repository overrides used, and nothing is written.
/// </summary>
/// <remarks>
/// <para>
/// It acts on repository saves only. Seeding, replication and migrations save through the context itself and are not
/// locked, as before.
/// </para>
/// <para>
/// It is a guard: <c>SuppressAllSaveHandlers()</c> and <c>SuppressSaveHandlers(stages)</c> do not switch it off. Only
/// code that names it does, with <c>SuppressSaveHandler&lt;FeatureLock&gt;()</c>.
/// </para>
/// <para>
/// Its constructor takes nothing, so it needs no services to be created and is there on every identity context, also
/// one that a tool or a test builds by hand. What the check needs it asks of the save's services
/// (<see cref="RowSet{TEntity}.Services"/>) when a repository's save reaches it: the feature locking of the host, and
/// the localizer for the message. A context that has no services has no feature locking to enforce.
/// </para>
/// </remarks>
[Guard]
public class FeatureLock :
    IBeforeSaveHandler<Country>,
    IBeforeSaveHandler<Region>,
    IBeforeSaveHandler<City>,
    IBeforeSaveHandler<Brand>,
    IBeforeSaveHandler<Department>,
    IBeforeSaveHandler<Service>,
    IBeforeSaveHandler<Company>,
    IBeforeSaveHandler<CompanyBranch>,
    IBeforeSaveHandler<App>,
    IBeforeSaveHandler<AccessTree>,
    IBeforeSaveHandler<User>,
    IBeforeSaveHandler<Team>,
    IBeforeSaveHandler<CompanyCalendar>
{
    public ValueTask BeforeSaveAsync(PendingRows<Country> rows, CancellationToken cancellationToken) => Check(rows);
    public ValueTask BeforeSaveAsync(PendingRows<Region> rows, CancellationToken cancellationToken) => Check(rows);
    public ValueTask BeforeSaveAsync(PendingRows<City> rows, CancellationToken cancellationToken) => Check(rows);
    public ValueTask BeforeSaveAsync(PendingRows<Brand> rows, CancellationToken cancellationToken) => Check(rows);
    public ValueTask BeforeSaveAsync(PendingRows<Department> rows, CancellationToken cancellationToken) => Check(rows);
    public ValueTask BeforeSaveAsync(PendingRows<Service> rows, CancellationToken cancellationToken) => Check(rows);
    public ValueTask BeforeSaveAsync(PendingRows<Company> rows, CancellationToken cancellationToken) => Check(rows);
    public ValueTask BeforeSaveAsync(PendingRows<CompanyBranch> rows, CancellationToken cancellationToken) => Check(rows);
    public ValueTask BeforeSaveAsync(PendingRows<App> rows, CancellationToken cancellationToken) => Check(rows);
    public ValueTask BeforeSaveAsync(PendingRows<AccessTree> rows, CancellationToken cancellationToken) => Check(rows);
    public ValueTask BeforeSaveAsync(PendingRows<User> rows, CancellationToken cancellationToken) => Check(rows);
    public ValueTask BeforeSaveAsync(PendingRows<Team> rows, CancellationToken cancellationToken) => Check(rows);
    public ValueTask BeforeSaveAsync(PendingRows<CompanyCalendar> rows, CancellationToken cancellationToken) => Check(rows);

    private static ValueTask Check<TEntity>(PendingRows<TEntity> rows) where TEntity : class
    {
        if (!rows.IsRepositorySave || rows.Count == 0)
            return ValueTask.CompletedTask;

        // A host that configured no feature locking has nothing to enforce, and neither has a context without services.
        if (rows.Services.GetService<ShiftIdentityFeatureLocking>() is not { } featureLocking)
            return ValueTask.CompletedTask;

        if (featureLocking.GetLockedMessageKey(typeof(TEntity)) is not { } messageKey)
            return ValueTask.CompletedTask;

        var localizer = rows.Services.GetRequiredService<ShiftIdentityLocalizer>();

        throw new ShiftEntityException(new Message(localizer["Error"], localizer[messageKey]));
    }
}
