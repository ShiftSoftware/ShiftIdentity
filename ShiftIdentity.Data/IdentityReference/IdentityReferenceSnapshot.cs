using System.Globalization;

namespace ShiftSoftware.ShiftIdentity.Data.IdentityReference;

/// <summary>
/// The in-memory <see cref="IIdentityReferenceSnapshot{TModel}"/>: a dictionary copied at load time and
/// never written again.
///
/// <para>It takes its own copy on construction rather than holding the source dictionary. The source is a
/// live cache that other callers keep filling, and a snapshot that changed underneath its holder would
/// defeat the only reason it exists.</para>
/// </summary>
public sealed class IdentityReferenceSnapshot<TModel> : IIdentityReferenceSnapshot<TModel>
    where TModel : class
{
    private readonly Dictionary<string, TModel> byId;

    public IdentityReferenceSnapshot(
        IEnumerable<KeyValuePair<string, TModel>> items,
        IdentityLifecycleFilter filter,
        DateTimeOffset? loadedAt = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        this.byId = new Dictionary<string, TModel>(StringComparer.Ordinal);
        foreach (var item in items)
            this.byId[item.Key] = item.Value;

        this.Filter = filter;
        this.LoadedAt = loadedAt ?? DateTimeOffset.UtcNow;
    }

    public IdentityLifecycleFilter Filter { get; }

    public DateTimeOffset LoadedAt { get; }

    public IReadOnlyDictionary<string, TModel> ById => this.byId;

    public TModel? Find(string id)
        => string.IsNullOrWhiteSpace(id) ? null : this.byId.GetValueOrDefault(id);

    // Invariant culture, always. Document ids are written by the replication mapper as ID.ToString(), and a
    // host running under a culture with its own digit shapes must key the same rows as one running under
    // en-US.
    public TModel? Find(long id)
        => this.Find(id.ToString(CultureInfo.InvariantCulture));

    public void Dispose()
    {
        // Nothing to release: the snapshot is a plain dictionary. The interface is disposable so that a
        // parquet or DuckDB snapshot, which does hold a resource, can be swapped in without every caller
        // changing.
    }
}
