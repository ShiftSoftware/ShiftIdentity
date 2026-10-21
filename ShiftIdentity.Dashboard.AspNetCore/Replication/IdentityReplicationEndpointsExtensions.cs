using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftEntity.CosmosDbReplication.Services;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Replication;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Replication;

/// <summary>
/// Turns on the dashboard's <c>api/IdentityReplication</c> endpoints, which run the Identity catch-up from the API: one
/// endpoint per entity, each open to whoever may write that entity, and <c>all</c>, which runs every entity the caller
/// may write. They let an admin fill or repair the Identity containers with their own token, for example a new
/// container before the Functions app that reads it is deployed.
/// <code>
/// builder.Services.AddShiftIdentityReplicationEndpoints&lt;DB&gt;(sp =&gt; sp.GetRequiredService&lt;MyCosmosClient&gt;(), databaseId);
/// </code>
/// <c>MapShiftIdentityDashboard()</c> then maps them; without this call it maps none. The host must also register
/// the replication engine (<c>AddShiftEntityCosmosDbReplication&lt;DB&gt;()</c>), as it does for the save side.
/// </summary>
public static class IdentityReplicationEndpointsExtensions
{
    /// <param name="client">The Cosmos client the host's save-side replication writes through.</param>
    /// <param name="databaseId">The Identity Cosmos database.</param>
    /// <param name="entities">
    /// The entities this host replicates. Every entity by default; a host that does not replicate users passes
    /// <see cref="IdentityReplicationEntities.Organization"/>. Only these get an endpoint.
    /// </param>
    public static IServiceCollection AddShiftIdentityReplicationEndpoints<TDbContext>(this IServiceCollection services,
        Func<IServiceProvider, CosmosClient> client, string databaseId, IEnumerable<IdentityReplicationEntity>? entities = null)
        where TDbContext : ShiftIdentityDbContext
    {
        services.AddSingleton(new IdentityReplicationEndpointsRegistration(
            client,
            databaseId,
            [.. IdentityReplicationEntities.All.Intersect(entities ?? IdentityReplicationEntities.All)],
            (cosmos, cosmosClient, selected, updateAll) => cosmos.ReplicateAsync<TDbContext>(cosmosClient, databaseId, selected, updateAll)));

        return services;
    }
}

/// <summary>What <see cref="IdentityReplicationEndpointsExtensions.AddShiftIdentityReplicationEndpoints{TDbContext}"/> registered.</summary>
internal sealed record IdentityReplicationEndpointsRegistration(
    Func<IServiceProvider, CosmosClient> Client,
    string DatabaseId,
    IReadOnlyList<IdentityReplicationEntity> Entities,
    Func<CosmosDBReplication, CosmosClient, IEnumerable<IdentityReplicationEntity>, bool, Task<IdentityCatchUpReport>> Run);
