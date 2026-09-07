using ShiftSoftware.ShiftEntity.Model.Replication;

namespace ShiftSoftware.ShiftIdentity.Data.IdentityReference.Cosmos;

/// <summary>
/// Database and container names for <see cref="CosmosIdentityReferenceSource{TCosmosClient}"/>.
///
/// <para>Deliberately Cosmos-named: these are Cosmos concepts and belong to that backend, not to
/// <see cref="IIdentityReferenceSource"/>. Another backend brings its own options type.</para>
///
/// <para>Every value defaults to the shared <see cref="IdentityDatabaseAndContainerNames"/> constants, so a
/// host that has not renamed anything configures nothing.</para>
/// </summary>
public class CosmosIdentityReferenceOptions
{
    /// <summary>
    /// How long a row read from Cosmos may be answered from memory before it is read again. Zero, the
    /// default, means what the source remembers lives and dies with the source itself — with the default
    /// per-request registration, that is the end of the request.
    ///
    /// <para><b>This one value decides both how long reads are remembered and how widely they are
    /// shared</b>, and that is on purpose:</para>
    ///
    /// <list type="bullet">
    ///   <item><b>Left at zero</b> — each source keeps its own, so a web host collapses the duplicate
    ///     lookups inside one request and starts the next request knowing nothing. Nothing can go stale
    ///     because nothing survives long enough to.</item>
    ///   <item><b>Set to a duration</b> — every request in the process reads from one shared set of rows,
    ///     each dropped once it is older than this. A web host stops re-scanning containers per request,
    ///     and an edit takes at most this long to be seen.</item>
    /// </list>
    ///
    /// <para>There is deliberately no third setting. Sharing rows across requests without an expiry would
    /// mean an edit is never seen until the process restarts, and the cost of that is invisible: a stale
    /// name looks exactly like a correct one. Setting this is the only way to widen the sharing, so the
    /// expiry always comes with it.</para>
    ///
    /// <para>Bulk runs are unaffected either way. A snapshot is read through to Cosmos when it is taken and
    /// is pinned from then on, because a roster that expired part-way through a run would give a report
    /// whose first and last rows disagree about the same branch name.</para>
    ///
    /// <para>One value covers every family. Per-family durations were considered and rejected: their
    /// misconfiguration is silent, and countries, companies and branches do not change often enough to
    /// differ.</para>
    /// </summary>
    public TimeSpan CacheTimeToLive { get; set; } = TimeSpan.Zero;

    public string DatabaseName { get; set; } = IdentityDatabaseAndContainerNames.DatabaseName;

    public string CountryContainerName { get; set; } = IdentityDatabaseAndContainerNames.CountryContainerName;

    public string CompanyContainerName { get; set; } = IdentityDatabaseAndContainerNames.CompanyContainerName;

    public string CompanyBranchContainerName { get; set; } = IdentityDatabaseAndContainerNames.CompanyBranchContainerName;

    public string ServiceContainerName { get; set; } = IdentityDatabaseAndContainerNames.ServiceContainerName;

    public string DepartmentContainerName { get; set; } = IdentityDatabaseAndContainerNames.DepartmentContainerName;

    public string TeamContainerName { get; set; } = IdentityDatabaseAndContainerNames.TeamContainerName;

    public string BrandContainerName { get; set; } = IdentityDatabaseAndContainerNames.BrandContainerName;

    public string UserContainerName { get; set; } = IdentityDatabaseAndContainerNames.UserContainerName;
}
