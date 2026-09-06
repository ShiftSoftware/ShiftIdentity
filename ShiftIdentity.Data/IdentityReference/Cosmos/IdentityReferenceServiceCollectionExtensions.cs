using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        services.TryAddScoped<IIdentityReferenceSource, CosmosIdentityReferenceSource<TCosmosClient>>();

        return services;
    }
}
