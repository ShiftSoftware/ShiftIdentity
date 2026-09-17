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
    /// Registers THIS ASSEMBLY's generated mapper — the class ShiftMapper's generator wrote holding the 19 pairs
    /// <see cref="ShiftIdentityReplicationMapper"/> declares — together with <see cref="Mapper"/> and
    /// <see cref="IMapper"/> over everything registered so far, which is how the replication pipeline finds it.
    /// Nothing is named: <c>AddShiftMapper</c> reads the generated class out of the assembly's own metadata, and the
    /// mapper class itself is never registered — the generated mapper builds it on first use.
    /// <para>
    /// A host normally never calls this: the identity registrations do — <c>AddShiftIdentityDashboard&lt;TDbContext&gt;()</c>
    /// on the API side (the save trigger's <c>SetUpAllIdentityReplications</c>) and the Functions worker's
    /// <c>AddShiftIdentity(issuer, key)</c> (the catch-up <c>ReplicateAllAsync</c>). It is public for a host that
    /// wires replication without either, and it is IDEMPOTENT: ShiftMapper keeps ONE registry per collection and a
    /// second registration of the same assembly's generated mapper changes nothing, whichever lifetime it asks for —
    /// the first registration wins. So both identity registrations and an explicit host call coexist without a
    /// guard of this method's own.
    /// </para>
    /// <para>
    /// A host with a generator of its own does not need to know about this: its generated mapper already carries
    /// these 19 pairs, re-baked with the host's rules, because ShiftMapper reads every mapper class of every
    /// referenced package into it (<c>MapperDiscovery.All</c>, the default). <see cref="Mapper"/> puts that one
    /// FIRST and this assembly's second, so the host's answers and this registration is the fallback — for a host
    /// with no generator, or one that maps only through <see cref="IMapper"/>. What ShiftMapper refuses (SM0042 at
    /// build time) is a second mapper class writing its OWN <c>CreateMap</c> for one of these pairs.
    /// </para>
    /// <para>
    /// Singleton by default: the mapper class takes no dependencies, and its maps read nothing scoped. When a host
    /// registers a scoped generated mapper of its own, the <see cref="Mapper"/> ShiftMapper builds over both takes
    /// the shorter lifetime.
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

        return services.AddShiftMapper(o => o.Lifetime = lifetime);
    }
}
