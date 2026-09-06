namespace ShiftSoftware.ShiftIdentity.Data.IdentityReference;

/// <summary>
/// A pinned, immutable roster of one identity family, owned by the caller for as long as it holds it.
///
/// <para><b>What it is for.</b> A bulk run makes millions of resolver calls and cannot reach the store for
/// each one. It loads a snapshot once and answers from memory for the whole run.</para>
///
/// <para><b>Why it is pinned rather than time-limited.</b> A cache that expires part-way through a run
/// produces a file whose first row and last row disagree about the same branch name, with nothing in the
/// file to say which is right. For a report, stale-but-consistent beats fresh-but-torn. A snapshot never
/// refreshes itself; the caller decides when to load a new one.</para>
///
/// <para><b><see cref="Find(string)"/> is not resolve.</b> It answers only from the rows this snapshot
/// holds, which is whatever <see cref="Filter"/> let in. A snapshot loaded with
/// <see cref="IdentityLifecycleFilter.ActiveOnly"/> and then used to look up ids will return null for
/// every terminated company in it — the exact failure the resolve/roster split exists to prevent. If you
/// are building a snapshot to resolve ids against, load it with <see cref="IdentityLifecycleFilter.All"/>
/// and read the lifecycle state off the model.</para>
///
/// <para>Disposable because a backend other than an in-memory dictionary may hold a real resource — a
/// parquet reader or a database connection. The in-memory implementation has nothing to release, but
/// callers should dispose regardless so a change of backend does not become a leak.</para>
/// </summary>
/// <typeparam name="TModel">The identity model this snapshot holds.</typeparam>
public interface IIdentityReferenceSnapshot<TModel> : IDisposable
    where TModel : class
{
    /// <summary>The lifecycle filter this snapshot was loaded with. Rows outside it are simply not here.</summary>
    IdentityLifecycleFilter Filter { get; }

    /// <summary>When the snapshot was taken. It does not change for the life of the snapshot.</summary>
    DateTimeOffset LoadedAt { get; }

    /// <summary>Every row in the snapshot, keyed by the store's document id.</summary>
    IReadOnlyDictionary<string, TModel> ById { get; }

    /// <summary>
    /// Looks one row up within this snapshot. Returns null when the id is absent — which includes rows
    /// that exist but were excluded by <see cref="Filter"/>. See the type remarks before using this to
    /// resolve ids.
    /// </summary>
    TModel? Find(string id);

    /// <inheritdoc cref="Find(string)"/>
    TModel? Find(long id);
}
