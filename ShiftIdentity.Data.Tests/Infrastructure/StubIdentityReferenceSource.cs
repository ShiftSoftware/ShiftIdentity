using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Data.IdentityReference;
using ShiftSoftware.ShiftIdentity.Data.IdentityReference.Cosmos;

namespace ShiftIdentity.Data.Tests.Infrastructure;

/// <summary>
/// A <see cref="CosmosIdentityReferenceSource{TCosmosClient}"/> with Cosmos replaced at the two methods
/// that read it, so the caching above them can be tested for what it is: how often the store is touched,
/// what happens when two callers arrive at once, and what is kept when a read fails.
///
/// <para>Everything else about the source is the real thing — the same public methods, the same cache, the
/// same lifecycle filtering. Only the reads are stubbed, and each one is counted.</para>
/// </summary>
internal sealed class StubIdentityReferenceSource : CosmosIdentityReferenceSource<StubCosmosClient>
{
    private readonly Dictionary<Type, List<ReplicationModel>> rows = new();

    private int familyReads;
    private int itemReads;

    public StubIdentityReferenceSource(
        IdentityReferenceCache? cache = null,
        CosmosIdentityReferenceOptions? options = null)
        : base(new StubCosmosClient(), Options.Create(options ?? new CosmosIdentityReferenceOptions()), cache)
    {
    }

    /// <summary>How many times a whole family has been read out of the store.</summary>
    public int FamilyReads => Volatile.Read(ref this.familyReads);

    /// <summary>How many times a single row has been read out of the store.</summary>
    public int ItemReads => Volatile.Read(ref this.itemReads);

    /// <summary>Held by every read while it is set, so several callers can be lined up on one load.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>Makes the next read of either kind throw, once.</summary>
    public bool FailNextRead { get; set; }

    public void Seed<TModel>(params TModel[] models)
        where TModel : ReplicationModel
        => this.rows[typeof(TModel)] = models.Cast<ReplicationModel>().ToList();

    internal override async Task<IReadOnlyList<TModel>> ReadRowsAsync<TModel>(
        string containerName,
        string? itemType,
        string? id,
        CancellationToken cancellationToken)
    {
        if (id is null)
            Interlocked.Increment(ref this.familyReads);
        else
            Interlocked.Increment(ref this.itemReads);

        await this.WaitAndMaybeFailAsync().ConfigureAwait(false);

        var rows = this.RowsOf<TModel>();

        return id is null ? rows.ToList() : rows.Where(row => row.id == id).ToList();
    }

    private async Task WaitAndMaybeFailAsync()
    {
        if (this.Gate is { } gate)
            await gate.Task.ConfigureAwait(false);

        if (this.FailNextRead)
        {
            this.FailNextRead = false;
            throw new InvalidOperationException("The store is unreachable.");
        }
    }

    private IEnumerable<TModel> RowsOf<TModel>()
        where TModel : ReplicationModel
        => this.rows.TryGetValue(typeof(TModel), out var seeded) ? seeded.Cast<TModel>() : [];
}

/// <summary>
/// Stands in for a <see cref="CosmosClient"/> that is never called: the source under test has both of its
/// reads replaced, so nothing reaches this. It exists because the source is generic over a client type.
/// </summary>
internal sealed class StubCosmosClient : CosmosClient
{
}
