using Microsoft.Extensions.DependencyInjection;
using ShiftMapper;

namespace ShiftSoftware.ShiftIdentity.Data.Replication;

/// <summary>
/// Registers the mapper ShiftIdentity's Cosmos replication maps through.
/// </summary>
public static class IdentityReplicationMapperServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ShiftIdentityReplicationMapper"/> — the ready-made mapper carrying
    /// <see cref="IdentityReplicationProfile"/> — under its own type and under <see cref="IShiftMapper"/>, which is
    /// how the replication pipeline finds it.
    /// <para>
    /// A host normally never calls this: the identity registrations do — <c>AddShiftIdentityDashboard&lt;TDbContext&gt;()</c>
    /// on the API side (the save trigger's <c>SetUpAllIdentityReplications</c>) and the Functions worker's
    /// <c>AddShiftIdentity(issuer, key)</c> (the catch-up <c>ReplicateAllAsync</c>). It is public for a host that
    /// wires replication without either, and it is IDEMPOTENT so that path cannot double-register: once the mapper is
    /// in the collection every later call is a no-op, whichever lifetime it asks for — the first registration wins.
    /// </para>
    /// <para>
    /// Singleton by default: the profile takes no dependencies, and its maps read nothing scoped. A host whose own
    /// mapper adds the profile keeps working alongside this one, because the pipeline uses the last registered
    /// <see cref="IShiftMapper"/> that declares the pair it needs.
    /// </para>
    /// </summary>
    public static IServiceCollection AddShiftIdentityReplicationMapper(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        ArgumentNullException.ThrowIfNull(services);

        //Keyed on the mapper's own type rather than IShiftMapper: another mapper under the interface is the
        //application's, and this registration must neither replace it nor be skipped because of it.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShiftIdentityReplicationMapper)))
            return services;

        return services.AddShiftMapper<ShiftIdentityReplicationMapper>(lifetime);
    }
}
