using Microsoft.Extensions.DependencyInjection;
using ShiftMapper;

namespace ShiftSoftware.ShiftIdentity.Data.Replication;

/// <summary>
/// Registers the mapper ShiftIdentity's Cosmos replication maps through — THE PACKAGE REGISTERING ITSELF, the way
/// ShiftMapper expects a framework's own <c>AddXxx</c> to.
/// </summary>
public static class IdentityReplicationMapperServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ShiftIdentityReplicationMapper"/> under its own type and under <see cref="IShiftMapper"/>,
    /// which is how the replication pipeline finds it.
    /// <para>
    /// A host normally never calls this: the identity registrations do — <c>AddShiftIdentityDashboard&lt;TDbContext&gt;()</c>
    /// on the API side (the save trigger's <c>SetUpAllIdentityReplications</c>) and the Functions worker's
    /// <c>AddShiftIdentity(issuer, key)</c> (the catch-up <c>ReplicateAllAsync</c>). It is public for a host that
    /// wires replication without either, and it is IDEMPOTENT so that path cannot double-register: once the mapper is
    /// in the collection every later call is a no-op, whichever lifetime it asks for — the first registration wins.
    /// That matters more than it used to: ShiftMapper keeps ONE registry per collection and refuses the same mapper
    /// registered twice from the same assembly, so the guard is what lets both identity registrations and an
    /// explicit host call coexist.
    /// </para>
    /// <para>
    /// The registration is made FROM THIS ASSEMBLY — the mapper's own — which ShiftMapper treats as the package's
    /// fallback registration: a host that registers <see cref="ShiftIdentityReplicationMapper"/> itself
    /// (<c>o.AddMapper&lt;ShiftIdentityReplicationMapper&gt;()</c>, which gives it an adapter carrying the host's own
    /// packs) wins in either order, and the guard above yields to it the same way. A host whose own mapper INCLUDES
    /// this one keeps working alongside it too: <see cref="IShiftMapper"/> then resolves to a composite that
    /// dispatches each pair to the first registered mapper declaring it, and a pair reached two ways through one
    /// declaration runs the same map whichever answers.
    /// </para>
    /// <para>
    /// Singleton by default: the mapper takes no dependencies, and its maps read nothing scoped. When a host
    /// registers a scoped mapper of its own, the composite ShiftMapper builds over both takes the shorter lifetime.
    /// </para>
    /// <para>
    /// Written as an inline options lambda rather than the generic short form on purpose. The generator reads either,
    /// but at run time the short form finds the registering assembly through <c>Assembly.GetCallingAssembly()</c>,
    /// which the JIT can hand a different frame once this method is inlined into an identity registration in another
    /// assembly; the lambda's closure type pins the registering assembly to this one whatever the JIT does. It is also
    /// the shape a shared pack would be added to (<c>o.ShareConversions&lt;…&gt;()</c>) should the identity documents
    /// ever need one.
    /// </para>
    /// </summary>
    public static IServiceCollection AddShiftIdentityReplicationMapper(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        ArgumentNullException.ThrowIfNull(services);

        //Keyed on the mapper's own type rather than IShiftMapper: another mapper under the interface is the
        //application's, and this registration must neither replace it nor be skipped because of it. The host's own
        //adapter for THIS mapper also lands under this type, and yielding to it is exactly ShiftMapper's rule.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ShiftIdentityReplicationMapper)))
            return services;

        return services.AddShiftMapper(o =>
        {
            o.Lifetime = lifetime;
            o.AddMapper<ShiftIdentityReplicationMapper>();
        });
    }
}
