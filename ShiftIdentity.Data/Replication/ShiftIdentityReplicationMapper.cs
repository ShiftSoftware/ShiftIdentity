using ShiftMapper;

namespace ShiftSoftware.ShiftIdentity.Data.Replication;

/// <summary>
/// The ready-made ShiftMapper mapper for ShiftIdentity's Cosmos replication: <see cref="IdentityReplicationProfile"/>
/// and nothing else. The identity registrations (<c>AddShiftIdentityDashboard&lt;TDbContext&gt;()</c>, the Functions
/// worker's <c>AddShiftIdentity(issuer, key)</c>) register it through <c>AddShiftIdentityReplicationMapper()</c>, and
/// every delegate-less <c>Replicate</c> / <c>UpdateReference</c> / <c>UpdatePropertyReference</c> in
/// <c>SetUpAllIdentityReplications</c> and <c>ReplicateAllAsync</c> maps through it.
/// <para>
/// PARTIAL, because the ShiftMapper generator writes the other half — the real <c>Map</c> methods and the explicit
/// <see cref="IShiftMapper"/> implementation the replication pipeline resolves. That half is generated on THIS
/// assembly's build (the generator is referenced here), which is what makes the class usable as-is from a host that
/// only references the package.
/// </para>
/// <para>
/// A host that already has a mapper of its own does not need this one: <c>AddProfile&lt;IdentityReplicationProfile&gt;()</c>
/// from its constructor folds the same 19 pairs into that mapper. Both are fine to register side by side, too — the
/// replication pipeline consults every registered <see cref="IShiftMapper"/> and uses the last one that declares
/// the pair it needs.
/// </para>
/// </summary>
public partial class ShiftIdentityReplicationMapper : ShiftMapperBase
{
    public ShiftIdentityReplicationMapper() => AddProfile<IdentityReplicationProfile>();
}
