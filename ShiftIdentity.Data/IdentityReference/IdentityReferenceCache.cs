using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace ShiftSoftware.ShiftIdentity.Data.IdentityReference;

/// <summary>
/// Where an <see cref="IIdentityReferenceSource"/> keeps what it has already read. Internal plumbing: the
/// service is still the service, this is only the part of it that remembers.
///
/// <para><b>Two lifetimes, and the time-to-live picks between them.</b> A source configured with no TTL
/// builds one of these for itself, so what it remembers is thrown away when the source is — with the
/// default per-request registration, at the end of the request. A source configured with a TTL is handed
/// the one registered for the whole process, shared by every request, and each entry is dropped once it is
/// older than the TTL. Those are the only two arrangements, which is the point: the only way to make what
/// is remembered outlive a request is to set the expiry that keeps it honest. There is no configuration
/// that gives a cache which lives until restart and never notices an edit.</para>
///
/// <para><b>A family's superset is what a resolve reads.</b> Each family holds one unfiltered dictionary —
/// every row, deleted and terminated included — plus up to three filtered views derived from it. Resolve
/// reads the superset and the single-row entries; it never reads a view. That is not a detail to tidy up
/// later: a filtered set reaching a resolve is how every historical reference to a closed company turns
/// into a blank name. The views hang off the family entry rather than living in their own dictionary
/// precisely so they cannot outlive, or be mistaken for, the superset they came from.</para>
///
/// <para><b>Nothing on the hit path allocates or locks.</b> A hit is a lookup on a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by a struct, a comparison against a timestamp, and
/// a reference cast of an already-completed <see cref="Task{TResult}"/> that is handed back as it is. The
/// task object is cached, not just the value inside it, so no wrapper is built per call. Single-flight,
/// expiry and eviction all live on the miss path.</para>
///
/// <para><b>A failed load is evicted, never remembered.</b> Single-flight means concurrent callers share
/// one in-flight load; if that load throws, the entry holding it is removed, so the next caller retries
/// against the store instead of being handed the same failure until something else clears it.</para>
/// </summary>
internal sealed class IdentityReferenceCache
{
    private readonly ConcurrentDictionary<IdentityReferenceFamilyKey, FamilyEntry> families = new();
    private readonly ConcurrentDictionary<IdentityReferenceItemKey, ItemEntry> items = new();
    private readonly TimeSpan timeToLive;
    private readonly Func<long> clock;

    /// <param name="timeToLive">
    /// How long an entry stays usable. Anything at or below zero, and <see cref="Timeout.InfiniteTimeSpan"/>,
    /// mean entries never expire on their own — which is right for a cache that dies with its request, and
    /// wrong for one shared across requests.
    /// </param>
    /// <param name="clock">
    /// Milliseconds from any fixed origin. Defaults to <see cref="Environment.TickCount64"/>; tests pass
    /// their own so expiry can be exercised without waiting for it.
    /// </param>
    public IdentityReferenceCache(TimeSpan timeToLive, Func<long>? clock = null)
    {
        this.timeToLive = timeToLive;
        this.clock = clock ?? (static () => Environment.TickCount64);
    }

    /// <summary>Whether entries in this cache expire on their own.</summary>
    public bool Expires => this.timeToLive > TimeSpan.Zero && this.timeToLive != Timeout.InfiniteTimeSpan;

    // ---- The hit path --------------------------------------------------------------------------------

    /// <summary>
    /// The filtered roster for one family and filter, or null when it is not cached. Never consulted by a
    /// resolve.
    /// </summary>
    public Task<IReadOnlyDictionary<string, TModel>>? TryGetRoster<TModel>(in IdentityReferenceFamilyKey key, IdentityLifecycleFilter lifecycle)
        where TModel : class
    {
        if (!this.families.TryGetValue(key, out var entry) || !this.IsFresh(entry.ExpiresAt))
            return null;

        return Volatile.Read(ref entry.Views[ViewSlot(lifecycle)]) as Task<IReadOnlyDictionary<string, TModel>>;
    }

    /// <summary>
    /// The unfiltered superset for one family, or null when it is not cached or has not finished loading.
    /// This is the only roster a resolve may read, and it holds every row whatever its lifecycle state.
    /// </summary>
    public Task<IReadOnlyDictionary<string, TModel>>? TryGetSuperset<TModel>(in IdentityReferenceFamilyKey key)
        where TModel : class
    {
        if (!this.families.TryGetValue(key, out var entry) || !this.IsFresh(entry.ExpiresAt))
            return null;

        // IsValueCreated rather than Value: asking for a superset must never start loading one. A resolve
        // that tripped a cold full-family load would be doing the thing the contract forbids.
        if (!entry.Superset.IsValueCreated)
            return null;

        return entry.Superset.Value as Task<IReadOnlyDictionary<string, TModel>>;
    }

    /// <summary>
    /// One row by id, or null when it is not cached. A cached miss is a completed task whose result is
    /// null, so a row known to be absent is not looked up again.
    /// </summary>
    public Task<TModel?>? TryGetItem<TModel>(in IdentityReferenceItemKey key)
        where TModel : class
    {
        if (!this.items.TryGetValue(key, out var entry) || !this.IsFresh(entry.ExpiresAt))
            return null;

        return entry.Row as Task<TModel?>;
    }

    // ---- The miss path -------------------------------------------------------------------------------

    /// <summary>
    /// Loads a family if nobody else is already, filters it, and remembers both. Concurrent callers on the
    /// same family share one load and therefore one query, whatever filters they each asked for; if that
    /// load fails, the entry is evicted so the next caller starts a new one.
    /// </summary>
    /// <param name="key">The family being read.</param>
    /// <param name="lifecycle">The filter this caller asked for.</param>
    /// <param name="load">Reads the whole family, unfiltered. Never handed a caller's cancellation token — see the remarks on this type.</param>
    /// <param name="filter">Derives the caller's view from the unfiltered superset.</param>
    /// <param name="cancellationToken">Cancels this caller's wait, not the shared load.</param>
    public async Task<IReadOnlyDictionary<string, TModel>> GetOrLoadRosterAsync<TModel>(
        IdentityReferenceFamilyKey key,
        IdentityLifecycleFilter lifecycle,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, TModel>>> load,
        Func<IReadOnlyDictionary<string, TModel>, IdentityLifecycleFilter, IReadOnlyDictionary<string, TModel>> filter,
        CancellationToken cancellationToken)
        where TModel : class
    {
        var entry = this.GetOrCreateFamilyEntry(key, load);
        var supersetTask = (Task<IReadOnlyDictionary<string, TModel>>)entry.Superset.Value;
        var superset = await this.ObserveFamilyAsync(supersetTask, key, entry.Superset, cancellationToken).ConfigureAwait(false);

        var slot = ViewSlot(lifecycle);

        if (slot == ViewSlot(IdentityLifecycleFilter.All))
        {
            // All is the superset itself: the commonest roster read costs no copy and no second dictionary.
            this.PublishView(key, entry, slot, supersetTask);
            return superset;
        }

        var view = filter(superset, lifecycle);
        this.PublishView(key, entry, slot, Task.FromResult(view));

        return view;
    }

    /// <summary>
    /// Loads one row by id if nobody else is already. Same single-flight and same eviction on failure as a
    /// family load.
    /// </summary>
    public Task<TModel?> GetOrLoadItemAsync<TModel>(
        IdentityReferenceItemKey key,
        Func<CancellationToken, Task<TModel?>> load,
        CancellationToken cancellationToken)
        where TModel : class
    {
        var entry = this.GetOrCreateItemEntry(key, load);
        var task = (Task<TModel?>)entry.Loader.Value;

        return this.ObserveItemAsync(task, key, entry.Loader, cancellationToken);
    }

    // ---- Publication, refresh and eviction -----------------------------------------------------------

    /// <summary>
    /// Stores a family that has already been read — a refresh, or the read-through a snapshot does. Views
    /// derived from the previous superset go with it, and so do that family's single-row entries: they were
    /// read before this one and a resolve reads them first, so leaving them would let a refreshed family
    /// keep answering with the rows it was refreshed away from.
    /// </summary>
    public void PublishSuperset<TModel>(in IdentityReferenceFamilyKey key, IReadOnlyDictionary<string, TModel> rows)
        where TModel : class
    {
        this.families[key] = NewPublishedEntry(rows, this.NextExpiry());
        this.DropItems(in key);
    }

    /// <summary>
    /// Stores one row by id, or the knowledge that there is no such row, and hands back the task it stored
    /// — so a caller that has just served a row out of a family's superset can return the very object the
    /// next caller will be given, rather than building a second one.
    /// </summary>
    public Task<TModel?> PublishItem<TModel>(in IdentityReferenceItemKey key, TModel? row)
        where TModel : class
    {
        var task = Task.FromResult(row);

        this.items[key] = new ItemEntry(new Lazy<object>(task), this.NextExpiry());

        return task;
    }

    /// <summary>
    /// Replaces one row inside a cached superset, for a single-id refresh. Without this a refreshed row
    /// would be answered correctly by a resolve and wrongly by a roster, which is a worse state than either
    /// being stale. Copy-on-write, because a dictionary handed to callers is never written to after it is
    /// published; the cost is one copy of one family, on an explicit refresh.
    /// </summary>
    public void ReplaceInSuperset<TModel>(in IdentityReferenceFamilyKey key, string id, TModel? row)
        where TModel : class
    {
        if (this.TryGetSuperset<TModel>(in key) is not { IsCompletedSuccessfully: true } cached)
            return;

        var rebuilt = new Dictionary<string, TModel>(cached.Result, StringComparer.Ordinal);

        if (row is null)
            rebuilt.Remove(id);
        else
            rebuilt[id] = row;

        this.families[key] = NewPublishedEntry<TModel>(new ReadOnlyDictionary<string, TModel>(rebuilt), this.NextExpiry());
    }

    /// <summary>Forgets one family: its superset, its views and its single-row entries.</summary>
    public void DropFamily(in IdentityReferenceFamilyKey key)
    {
        this.families.TryRemove(key, out _);
        this.DropItems(in key);
    }

    // ---- Internals -----------------------------------------------------------------------------------

    private static FamilyEntry NewPublishedEntry<TModel>(IReadOnlyDictionary<string, TModel> rows, long expiresAt)
        where TModel : class
    {
        var task = Task.FromResult(rows);
        var entry = new FamilyEntry(new Lazy<object>(task), expiresAt);

        entry.Views[ViewSlot(IdentityLifecycleFilter.All)] = task;

        return entry;
    }

    /// <summary>
    /// Stores a view against the family entry it was derived from, so it expires exactly when that
    /// superset does. If the family has been refreshed or replaced meanwhile, the view is dropped rather
    /// than stored against a superset it does not match.
    /// </summary>
    private void PublishView(in IdentityReferenceFamilyKey key, FamilyEntry entry, int slot, object view)
    {
        if (this.families.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            Volatile.Write(ref entry.Views[slot], view);
    }

    private FamilyEntry GetOrCreateFamilyEntry<TModel>(
        IdentityReferenceFamilyKey key,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, TModel>>> load)
        where TModel : class
    {
        while (true)
        {
            if (this.families.TryGetValue(key, out var existing))
            {
                if (this.IsFresh(existing.ExpiresAt))
                    return existing;

                var replacement = this.NewFamilyEntry(key, load);

                if (this.families.TryUpdate(key, replacement, existing))
                    return replacement;

                continue;
            }

            var created = this.NewFamilyEntry(key, load);

            if (this.families.TryAdd(key, created))
                return created;
        }
    }

    private FamilyEntry NewFamilyEntry<TModel>(
        IdentityReferenceFamilyKey key,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, TModel>>> load)
        where TModel : class
    {
        Lazy<object>? loader = null;

        // The load runs detached from any one caller's cancellation token: callers share it, so letting the
        // first one to give up cancel it would fail everyone else waiting on the same query.
        loader = new Lazy<object>(
            () =>
            {
                var task = load(CancellationToken.None);
                this.EvictOnFailure(task, key, loader!);
                return task;
            },
            LazyThreadSafetyMode.ExecutionAndPublication);

        return new FamilyEntry(loader, this.NextExpiry());
    }

    private ItemEntry GetOrCreateItemEntry<TModel>(
        IdentityReferenceItemKey key,
        Func<CancellationToken, Task<TModel?>> load)
        where TModel : class
    {
        while (true)
        {
            if (this.items.TryGetValue(key, out var existing))
            {
                if (this.IsFresh(existing.ExpiresAt))
                    return existing;

                var replacement = this.NewItemEntry(key, load);

                if (this.items.TryUpdate(key, replacement, existing))
                    return replacement;

                continue;
            }

            var created = this.NewItemEntry(key, load);

            if (this.items.TryAdd(key, created))
                return created;
        }
    }

    private ItemEntry NewItemEntry<TModel>(
        IdentityReferenceItemKey key,
        Func<CancellationToken, Task<TModel?>> load)
        where TModel : class
    {
        Lazy<object>? loader = null;

        loader = new Lazy<object>(
            () =>
            {
                var task = load(CancellationToken.None);
                this.EvictOnFailure(task, key, loader!);
                return task;
            },
            LazyThreadSafetyMode.ExecutionAndPublication);

        return new ItemEntry(loader, this.NextExpiry());
    }

    private async Task<IReadOnlyDictionary<string, TModel>> ObserveFamilyAsync<TModel>(
        Task<IReadOnlyDictionary<string, TModel>> task,
        IdentityReferenceFamilyKey key,
        object loader,
        CancellationToken cancellationToken)
        where TModel : class
    {
        try
        {
            return cancellationToken.CanBeCanceled
                ? await task.WaitAsync(cancellationToken).ConfigureAwait(false)
                : await task.ConfigureAwait(false);
        }
        catch when (task.IsFaulted)
        {
            // The loader attaches an eviction continuation too; doing it here as well means the entry is
            // provably gone by the time the failure reaches the caller.
            this.EvictFamily(key, loader);
            throw;
        }
    }

    private async Task<TModel?> ObserveItemAsync<TModel>(
        Task<TModel?> task,
        IdentityReferenceItemKey key,
        object loader,
        CancellationToken cancellationToken)
        where TModel : class
    {
        try
        {
            return cancellationToken.CanBeCanceled
                ? await task.WaitAsync(cancellationToken).ConfigureAwait(false)
                : await task.ConfigureAwait(false);
        }
        catch when (task.IsFaulted)
        {
            this.EvictItem(key, loader);
            throw;
        }
    }

    private void EvictOnFailure(Task task, IdentityReferenceFamilyKey key, object loader)
        => task.ContinueWith(
            static (_, state) =>
            {
                var (cache, key, loader) = ((IdentityReferenceCache Cache, IdentityReferenceFamilyKey Key, object Loader))state!;
                cache.EvictFamily(key, loader);
            },
            (Cache: this, Key: key, Loader: loader),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private void EvictOnFailure(Task task, IdentityReferenceItemKey key, object loader)
        => task.ContinueWith(
            static (_, state) =>
            {
                var (cache, key, loader) = ((IdentityReferenceCache Cache, IdentityReferenceItemKey Key, object Loader))state!;
                cache.EvictItem(key, loader);
            },
            (Cache: this, Key: key, Loader: loader),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    /// <summary>
    /// Removes the entry only while it is still the one that failed. A retry that has already replaced it
    /// must survive the eviction of the load it replaced.
    /// </summary>
    private void EvictFamily(IdentityReferenceFamilyKey key, object loader)
    {
        if (this.families.TryGetValue(key, out var entry) && ReferenceEquals(entry.Superset, loader))
            this.families.TryRemove(new KeyValuePair<IdentityReferenceFamilyKey, FamilyEntry>(key, entry));
    }

    /// <inheritdoc cref="EvictFamily"/>
    private void EvictItem(IdentityReferenceItemKey key, object loader)
    {
        if (this.items.TryGetValue(key, out var entry) && ReferenceEquals(entry.Loader, loader))
            this.items.TryRemove(new KeyValuePair<IdentityReferenceItemKey, ItemEntry>(key, entry));
    }

    private void DropItems(in IdentityReferenceFamilyKey key)
    {
        foreach (var item in this.items.Keys)
        {
            if (item.ModelType == key.ModelType
                && item.DatabaseName == key.DatabaseName
                && item.ContainerName == key.ContainerName
                && item.ItemType == key.ItemType)
            {
                this.items.TryRemove(item, out _);
            }
        }
    }

    private bool IsFresh(long expiresAt) => expiresAt == long.MaxValue || this.clock() < expiresAt;

    private long NextExpiry()
        => this.Expires ? this.clock() + (long)this.timeToLive.TotalMilliseconds : long.MaxValue;

    /// <summary>
    /// Which view slot a filter uses. A value this build does not recognise lands on
    /// <see cref="IdentityLifecycleFilter.All"/>, matching how an unrecognised filter is applied: over-list
    /// rather than silently drop rows.
    /// </summary>
    private static int ViewSlot(IdentityLifecycleFilter lifecycle)
        => lifecycle is IdentityLifecycleFilter.ExcludeDeleted or IdentityLifecycleFilter.ActiveOnly ? (int)lifecycle : 0;

    private sealed class FamilyEntry(Lazy<object> superset, long expiresAt)
    {
        /// <summary>Resolves to <c>Task&lt;IReadOnlyDictionary&lt;string, TModel&gt;&gt;</c>, unfiltered.</summary>
        public readonly Lazy<object> Superset = superset;

        /// <summary>One slot per <see cref="IdentityLifecycleFilter"/>, each a view over the superset above.</summary>
        public readonly object?[] Views = new object?[3];

        public readonly long ExpiresAt = expiresAt;
    }

    private sealed class ItemEntry(Lazy<object> loader, long expiresAt)
    {
        /// <summary>Resolves to <c>Task&lt;TModel?&gt;</c>; a null result is a row known not to exist.</summary>
        public readonly Lazy<object> Loader = loader;

        public readonly long ExpiresAt = expiresAt;

        /// <summary>The load's task once it has started, or null — reading this never starts one.</summary>
        public object? Row => this.Loader.IsValueCreated ? this.Loader.Value : null;
    }
}

/// <summary>Identifies one family of rows in one container. A struct, so looking one up allocates nothing.</summary>
internal readonly record struct IdentityReferenceFamilyKey(
    Type ModelType,
    string DatabaseName,
    string ContainerName,
    string? ItemType);

/// <summary>Identifies one row. A struct, so looking one up allocates nothing.</summary>
internal readonly record struct IdentityReferenceItemKey(
    Type ModelType,
    string DatabaseName,
    string ContainerName,
    string? ItemType,
    string Id);
