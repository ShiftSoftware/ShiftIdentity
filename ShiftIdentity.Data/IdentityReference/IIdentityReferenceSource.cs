using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;

namespace ShiftSoftware.ShiftIdentity.Data.IdentityReference;

/// <summary>
/// Reads identity reference data — countries, regions, cities, companies, branches, services,
/// departments, teams, brands and users — for everything that needs to turn an identity id into a name.
///
/// <para><b>Not a Cosmos contract.</b> Cosmos is one implementation
/// (<c>CosmosIdentityReferenceSource</c>) and the default one, but nothing here names it or assumes it.
/// A parquet or DuckDB implementation is expected, and for that one the snapshot is the only mode it has.
/// <b>Choosing a backend is a registration, not an option:</b> the Cosmos registration adds itself only
/// when nothing else has claimed this interface, and another backend's package removes it and installs its
/// own — so whichever backend a host ends up with is the only one in the container, in any call order. A
/// host that picks a backend which cannot serve identity gets a resolution failure at startup, not blank
/// names at run time.</para>
///
/// <para>There are two different questions here, and the answer to one is wrong for the other.</para>
///
/// <para><b>1. Resolve by id — always answers. This never filters on lifecycle, and that is not an
/// oversight to be tidied up.</b></para>
///
/// <para>A row that references a company or branch is referencing it because that reference was real when
/// it was written. Closing a branch does not un-write the invoices raised at it. If resolve filtered — on
/// <c>IsDeleted</c>, on <c>TerminationDate</c>, on anything — then every historical row pointing at a
/// closed company would resolve to null, and null becomes the empty string by the time it reaches a
/// report. That is the estate-wide blank-name defect this contract was written to fix, and adding a
/// lifecycle filter to resolve would reintroduce it across every consumer at once — reports, exports,
/// public-facing pages alike — in a single package bump.</para>
///
/// <para>It is worse than the original defect, because the original was visibly blank. Filtering here also
/// produces the second failure: a lookup that used to answer starts returning null, and callers that treat
/// a missing name as a missing record drop the row entirely — no blank cell, no error, just a record that
/// is quietly absent from the output.</para>
///
/// <para>The lifecycle state is not hidden — it is on the model that comes back. Every identity model
/// carries <c>IsDeleted</c>, and companies and branches carry <c>TerminationDate</c>. A caller that wants
/// to render a closed branch as "&lt;name&gt; — closed 2024" reads it there. That is the whole point:
/// resolve hands you the row and the facts about it, and lets you decide.</para>
///
/// <para><b>2. Roster / list — takes a lifecycle filter.</b> Listing is a different question: "what is
/// there", usually to populate a picker, where a closed branch is noise. So these take an
/// <see cref="IdentityLifecycleFilter"/>.</para>
///
/// <para><b>The default is currently <see cref="IdentityLifecycleFilter.All"/>, and that is deliberate and
/// temporary.</b> Production today conflates two populations under <c>IsDeleted</c>: rows genuinely
/// deleted, and companies and branches that were terminated but recorded as deleted because
/// <c>TerminationDate</c> was not being used. Until those are separated in the data, an active-only
/// default would hide live historical references rather than closed ones. The default becomes
/// <see cref="IdentityLifecycleFilter.ActiveOnly"/> once that separation has happened. Pass the value you
/// want explicitly and the change will not move under you.</para>
///
/// <para><b>Careful with rosters used as resolvers.</b> Loading a filtered list and then looking ids up in
/// it is resolving, whatever the code looks like, and it fails the same way a filtering resolve would. If
/// that is the shape you need, list with <see cref="IdentityLifecycleFilter.All"/> — or use
/// <c>Resolve…</c>, which cannot get this wrong.</para>
///
/// <para><b>Snapshots</b> (<c>Load…SnapshotAsync</c>) return a pinned immutable roster the caller owns and
/// disposes, for bulk runs that cannot reach the store per row. See
/// <see cref="IIdentityReferenceSnapshot{TModel}"/>.</para>
///
/// <para><b>Resolving one id must stay a one-row read.</b> Some callers resolve a single id as a hard
/// precondition — they read one row to decide whether an operation may proceed at all, behind a short
/// timeout, and abort rather than continue on a stale or missing value. Two things follow, and they bind
/// the implementation, not just the caller:</para>
///
/// <list type="number">
///   <item><b>Never implement resolve as "load the whole family, then index into it."</b> It is a tempting
///     shortcut, especially for a backend whose natural unit is a whole roster, and it silently turns a
///     one-row lookup into a cold full-family load — which can exceed a caller's timeout on the first call
///     and abort an operation that would otherwise have succeeded. A roster-native backend should still
///     offer a genuine single-row path, even where that means a narrower read against the same
///     source.</item>
///   <item><b>Do not move such a caller onto a snapshot.</b> A snapshot is stale by construction; a caller
///     that refuses to act on a stale value cannot be served from one.</item>
/// </list>
///
/// <para><b>Ids come in both shapes.</b> Some callers hold the id as a number, others hold the store's
/// document id as text. They are the same value — the replication mapper writes the document id as
/// <c>ID.ToString()</c> — so every resolve takes either, and no caller has to convert at the call
/// site.</para>
/// </summary>
public interface IIdentityReferenceSource
{
    // ---- Countries -------------------------------------------------------------------------------

    /// <summary>Resolves one country. Always answers, whatever its lifecycle state.</summary>
    Task<CountryModel?> ResolveCountryAsync(string id, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ResolveCountryAsync(string, CancellationToken)"/>
    Task<CountryModel?> ResolveCountryAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Lists countries, keyed by id, filtered by <paramref name="lifecycle"/>.</summary>
    Task<IReadOnlyDictionary<string, CountryModel>> GetCountriesAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default);

    /// <summary>Takes a pinned snapshot of countries. The caller owns and disposes it.</summary>
    Task<IIdentityReferenceSnapshot<CountryModel>> LoadCountriesSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default);

    // ---- Regions ---------------------------------------------------------------------------------

    /// <summary>Resolves one region. Always answers, whatever its lifecycle state.</summary>
    Task<RegionModel?> ResolveRegionAsync(string id, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ResolveRegionAsync(string, CancellationToken)"/>
    Task<RegionModel?> ResolveRegionAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Lists regions, keyed by id, filtered by <paramref name="lifecycle"/>.</summary>
    Task<IReadOnlyDictionary<string, RegionModel>> GetRegionsAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default);

    /// <summary>Takes a pinned snapshot of regions. The caller owns and disposes it.</summary>
    Task<IIdentityReferenceSnapshot<RegionModel>> LoadRegionsSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default);

    // ---- Cities ----------------------------------------------------------------------------------

    /// <summary>Resolves one city. Always answers, whatever its lifecycle state.</summary>
    Task<CityModel?> ResolveCityAsync(string id, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ResolveCityAsync(string, CancellationToken)"/>
    Task<CityModel?> ResolveCityAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Lists cities, keyed by id, filtered by <paramref name="lifecycle"/>.</summary>
    Task<IReadOnlyDictionary<string, CityModel>> GetCitiesAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default);

    /// <summary>Takes a pinned snapshot of cities. The caller owns and disposes it.</summary>
    Task<IIdentityReferenceSnapshot<CityModel>> LoadCitiesSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default);

    // ---- Companies -------------------------------------------------------------------------------

    /// <summary>
    /// Resolves one company. Always answers, whatever its lifecycle state — a terminated company still has
    /// a name, and historical rows referencing it are not errors. Read <c>TerminationDate</c> and
    /// <c>IsDeleted</c> off the result if the state matters to you.
    /// </summary>
    Task<CompanyModel?> ResolveCompanyAsync(string id, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ResolveCompanyAsync(string, CancellationToken)"/>
    Task<CompanyModel?> ResolveCompanyAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Lists companies, keyed by id, filtered by <paramref name="lifecycle"/>.</summary>
    Task<IReadOnlyDictionary<string, CompanyModel>> GetCompaniesAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default);

    /// <summary>Takes a pinned snapshot of companies. The caller owns and disposes it.</summary>
    Task<IIdentityReferenceSnapshot<CompanyModel>> LoadCompaniesSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default);

    // ---- Company branches ------------------------------------------------------------------------

    /// <summary>
    /// Resolves one company branch. Always answers, whatever its lifecycle state — a closed branch still
    /// has a name, and the invoices raised at it still reference it. Read <c>TerminationDate</c> and
    /// <c>IsDeleted</c> off the result if the state matters to you.
    /// </summary>
    Task<CompanyBranchModel?> ResolveCompanyBranchAsync(string id, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ResolveCompanyBranchAsync(string, CancellationToken)"/>
    Task<CompanyBranchModel?> ResolveCompanyBranchAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Lists company branches, keyed by id, filtered by <paramref name="lifecycle"/>.</summary>
    Task<IReadOnlyDictionary<string, CompanyBranchModel>> GetCompanyBranchesAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default);

    /// <summary>Takes a pinned snapshot of company branches. The caller owns and disposes it.</summary>
    Task<IIdentityReferenceSnapshot<CompanyBranchModel>> LoadCompanyBranchesSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default);

    // ---- Services --------------------------------------------------------------------------------

    /// <summary>Resolves one service. Always answers, whatever its lifecycle state.</summary>
    Task<ServiceModel?> ResolveServiceAsync(string id, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ResolveServiceAsync(string, CancellationToken)"/>
    Task<ServiceModel?> ResolveServiceAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Lists services, keyed by id, filtered by <paramref name="lifecycle"/>.</summary>
    Task<IReadOnlyDictionary<string, ServiceModel>> GetServicesAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default);

    /// <summary>Takes a pinned snapshot of services. The caller owns and disposes it.</summary>
    Task<IIdentityReferenceSnapshot<ServiceModel>> LoadServicesSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default);

    // ---- Departments -----------------------------------------------------------------------------

    /// <summary>Resolves one department. Always answers, whatever its lifecycle state.</summary>
    Task<DepartmentModel?> ResolveDepartmentAsync(string id, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ResolveDepartmentAsync(string, CancellationToken)"/>
    Task<DepartmentModel?> ResolveDepartmentAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Lists departments, keyed by id, filtered by <paramref name="lifecycle"/>.</summary>
    Task<IReadOnlyDictionary<string, DepartmentModel>> GetDepartmentsAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default);

    /// <summary>Takes a pinned snapshot of departments. The caller owns and disposes it.</summary>
    Task<IIdentityReferenceSnapshot<DepartmentModel>> LoadDepartmentsSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default);

    // ---- Teams -----------------------------------------------------------------------------------

    /// <summary>Resolves one team. Always answers, whatever its lifecycle state.</summary>
    Task<TeamModel?> ResolveTeamAsync(string id, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ResolveTeamAsync(string, CancellationToken)"/>
    Task<TeamModel?> ResolveTeamAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Lists teams, keyed by id, filtered by <paramref name="lifecycle"/>.</summary>
    Task<IReadOnlyDictionary<string, TeamModel>> GetTeamsAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default);

    /// <summary>Takes a pinned snapshot of teams. The caller owns and disposes it.</summary>
    Task<IIdentityReferenceSnapshot<TeamModel>> LoadTeamsSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default);

    // ---- Brands ----------------------------------------------------------------------------------

    /// <summary>Resolves one brand. Always answers, whatever its lifecycle state.</summary>
    Task<BrandModel?> ResolveBrandAsync(string id, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ResolveBrandAsync(string, CancellationToken)"/>
    Task<BrandModel?> ResolveBrandAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Lists brands, keyed by id, filtered by <paramref name="lifecycle"/>.</summary>
    Task<IReadOnlyDictionary<string, BrandModel>> GetBrandsAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default);

    /// <summary>Takes a pinned snapshot of brands. The caller owns and disposes it.</summary>
    Task<IIdentityReferenceSnapshot<BrandModel>> LoadBrandsSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default);

    // ---- Users -----------------------------------------------------------------------------------

    /// <summary>Resolves one user. Always answers, whatever its lifecycle state.</summary>
    Task<UserModel?> ResolveUserAsync(string id, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ResolveUserAsync(string, CancellationToken)"/>
    Task<UserModel?> ResolveUserAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Lists users, keyed by id, filtered by <paramref name="lifecycle"/>.</summary>
    Task<IReadOnlyDictionary<string, UserModel>> GetUsersAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default);

    /// <summary>Takes a pinned snapshot of users. The caller owns and disposes it.</summary>
    Task<IIdentityReferenceSnapshot<UserModel>> LoadUsersSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default);
}
