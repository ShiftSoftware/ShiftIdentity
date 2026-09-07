using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ShiftSoftware.ShiftIdentity.Data.IdentityReference.Cosmos;

/// <summary>
/// Registers the identity reference source.
///
/// <para><b>The backend is a registration, not an option.</b> This call installs the Cosmos reader as the
/// DEFAULT <see cref="IIdentityReferenceSource"/>, and only while nothing else has claimed that interface.
/// Another backend's package removes whatever is registered and installs its own, so every call order
/// lands on exactly one source:</para>
///
/// <list type="bullet">
///   <item>this call alone → Cosmos, what a host gets for doing nothing special;</item>
///   <item>this call, then another backend's → that package removes this default and adds its own;</item>
///   <item>another backend's, then this call → a source already exists, so the default is skipped;</item>
///   <item>this call twice → the surviving source stays whichever was chosen.</item>
/// </list>
///
/// <para><b>The source is registered per request, and what it remembers normally dies with the request.</b>
/// That is the default and it needs no configuration. Setting
/// <see cref="CosmosIdentityReferenceOptions.CacheTimeToLive"/> moves what is remembered to one set of rows
/// shared by the whole process, each dropped once it is older than that — so a web host stops re-reading
/// the same containers on every request, and an edit is picked up within the configured time. The two go
/// together on purpose: rows outlive a request only when something says when to stop trusting them.</para>
///
/// <para><b>Why not a "which storage" enum on an options object.</b> Because selecting a backend that
/// cannot actually serve identity in a given host would then fail silently: the container resolves, every
/// lookup returns nothing, and the names come out blank — which is the defect this whole contract exists
/// to remove. A host that never registers a source gets a resolution failure the first time it asks for
/// one. Loud beats silent.</para>
/// </summary>
public static class IdentityReferenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Cosmos identity reference source against the default <see cref="CosmosClient"/>.
    /// </summary>
    public static IServiceCollection AddIdentityReferenceSource(
        this IServiceCollection services,
        Action<CosmosIdentityReferenceOptions>? configure = null)
        => services.AddIdentityReferenceSource<CosmosClient>(configure);

    /// <summary>
    /// Registers the Cosmos identity reference source against a specific <see cref="CosmosClient"/>
    /// registration, for hosts that keep more than one.
    /// </summary>
    public static IServiceCollection AddIdentityReferenceSource<TCosmosClient>(
        this IServiceCollection services,
        Action<CosmosIdentityReferenceOptions>? configure = null)
        where TCosmosClient : CosmosClient
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = services.AddOptions<CosmosIdentityReferenceOptions>();

        if (configure is not null)
            builder.Configure(configure);

        // The one cache shared by the whole process. Built whether or not a TTL is configured, because the
        // options can be bound from configuration long after this line runs; it is only handed to a source
        // when there is a TTL to keep it honest.
        services.TryAddSingleton(sp => new IdentityReferenceCache(
            sp.GetRequiredService<IOptions<CosmosIdentityReferenceOptions>>().Value.CacheTimeToLive));

        services.TryAddScoped<IIdentityReferenceSource>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<CosmosIdentityReferenceOptions>>();

            // This is the whole of the lifetime rule, and it lives here so it cannot be half-applied: rows
            // are shared across requests only when a time-to-live says when to stop trusting them, and they
            // expire only when they are shared. With no TTL the source keeps its own rows and drops them
            // with the request, which is what it has always done.
            var sharedCache = options.Value.CacheTimeToLive > TimeSpan.Zero
                ? sp.GetRequiredService<IdentityReferenceCache>()
                : null;

            return new CosmosIdentityReferenceSource<TCosmosClient>(
                sp.GetRequiredService<TCosmosClient>(),
                options,
                sharedCache);
        });

        return services;
    }
}
