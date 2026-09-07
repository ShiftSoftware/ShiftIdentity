using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;

namespace ShiftSoftware.ShiftIdentity.Data.IdentityReference.Cosmos;

/// <summary>
/// The Cosmos implementation of <see cref="IIdentityReferenceSource"/>, and the default one.
///
/// <para><b>The cache holds the unfiltered superset; filtering happens on read.</b> If the lifecycle
/// filter were part of the cache key, one caller asking for every row would fill the cache with rows a
/// caller asking for active-only must not see, or the other way round — either way one request's filter
/// leaks into another's. Doubling the cache is the wrong fix for that. These sets are small — single-digit
/// countries, low tens of companies and branches — so filtering in memory costs nothing, keeps the filter a
/// pure view over one cached copy, and works the same way when a parquet backend arrives.</para>
///
/// <para><b>How long anything is remembered is one setting</b>, <see cref="CosmosIdentityReferenceOptions.CacheTimeToLive"/>,
/// and it decides both the duration and how widely rows are shared. Left at its default of zero, this
/// source remembers rows only for as long as it exists, which under the default per-request registration
/// is one request. Set to a duration, every request in the process reads one shared set of rows, each
/// dropped once it is older than that. There is no setting that shares rows indefinitely without
/// re-reading them.</para>
///
/// <para><b>Nothing on a cache hit allocates or locks.</b> A remembered read hands back the very same
/// <see cref="Task{TResult}"/> object it handed back last time — no dictionary copy, no wrapper task, no
/// lock. Single-flight, expiry, filtering and refresh all live on the paths that were going to touch
/// Cosmos anyway.</para>
///
/// <para>Generic over <typeparamref name="TCosmosClient"/> so a host that keeps more than one client can
/// say which to read identity through.</para>
/// </summary>
public class CosmosIdentityReferenceSource<TCosmosClient> : IIdentityReferenceSource
    where TCosmosClient : CosmosClient
{
    private static readonly ConcurrentDictionary<Type, Func<object, string>> IdValueResolvers = new();

    private readonly TCosmosClient cosmosClient;
    private readonly CosmosIdentityReferenceOptions options;
    private readonly IdentityReferenceCache cache;

    /// <summary>
    /// Builds a source that remembers rows for as long as it exists itself — so under the default
    /// per-request registration, for one request. If
    /// <see cref="CosmosIdentityReferenceOptions.CacheTimeToLive"/> is set, rows also expire after that;
    /// they are still private to this instance, because sharing them across a process is something the
    /// registration arranges, not something an instance can do for itself.
    /// </summary>
    public CosmosIdentityReferenceSource(TCosmosClient cosmosClient, IOptions<CosmosIdentityReferenceOptions> options)
        : this(cosmosClient, options, sharedCache: null)
    {
    }

    /// <param name="cosmosClient">The client identity is read through.</param>
    /// <param name="options">Database and container names, and the time-to-live.</param>
    /// <param name="sharedCache">
    /// The process-wide cache, when the host has configured a time-to-live and the registration therefore
    /// wants every request served from one set of rows. Null gives this instance its own.
    /// </param>
    internal CosmosIdentityReferenceSource(
        TCosmosClient cosmosClient,
        IOptions<CosmosIdentityReferenceOptions> options,
        IdentityReferenceCache? sharedCache)
    {
        ArgumentNullException.ThrowIfNull(cosmosClient);
        ArgumentNullException.ThrowIfNull(options);

        this.cosmosClient = cosmosClient;
        this.options = options.Value;
        this.cache = sharedCache ?? new IdentityReferenceCache(this.options.CacheTimeToLive);
    }

    // ---- Countries -----------------------------------------------------------------------------------

    public Task<CountryModel?> ResolveCountryAsync(string id, CancellationToken cancellationToken = default)
        => this.GetItemByIdAsync<CountryModel>(id, this.GetCountryContainerName(), CountryContainerItemTypes.Country, cancellationToken);

    public Task<CountryModel?> ResolveCountryAsync(long id, CancellationToken cancellationToken = default)
        => this.ResolveCountryAsync(ToDocumentId(id), cancellationToken);

    public Task<IReadOnlyDictionary<string, CountryModel>> GetCountriesAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => this.GetItemsAsync<CountryModel>(this.GetCountryContainerName(), CountryContainerItemTypes.Country, lifecycle, cancellationToken);

    public Task<IIdentityReferenceSnapshot<CountryModel>> LoadCountriesSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => this.LoadSnapshotAsync<CountryModel>(this.GetCountryContainerName(), CountryContainerItemTypes.Country, lifecycle, cancellationToken);

    // ---- Regions -------------------------------------------------------------------------------------

    public Task<RegionModel?> ResolveRegionAsync(string id, CancellationToken cancellationToken = default)
        => this.GetItemByIdAsync<RegionModel>(id, this.GetCountryContainerName(), CountryContainerItemTypes.Region, cancellationToken);

    public Task<RegionModel?> ResolveRegionAsync(long id, CancellationToken cancellationToken = default)
        => this.ResolveRegionAsync(ToDocumentId(id), cancellationToken);

    public Task<IReadOnlyDictionary<string, RegionModel>> GetRegionsAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => this.GetItemsAsync<RegionModel>(this.GetCountryContainerName(), CountryContainerItemTypes.Region, lifecycle, cancellationToken);

    public Task<IIdentityReferenceSnapshot<RegionModel>> LoadRegionsSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => this.LoadSnapshotAsync<RegionModel>(this.GetCountryContainerName(), CountryContainerItemTypes.Region, lifecycle, cancellationToken);

    // ---- Cities --------------------------------------------------------------------------------------

    public Task<CityModel?> ResolveCityAsync(string id, CancellationToken cancellationToken = default)
        => this.GetItemByIdAsync<CityModel>(id, this.GetCountryContainerName(), CountryContainerItemTypes.City, cancellationToken);

    public Task<CityModel?> ResolveCityAsync(long id, CancellationToken cancellationToken = default)
        => this.ResolveCityAsync(ToDocumentId(id), cancellationToken);

    public Task<IReadOnlyDictionary<string, CityModel>> GetCitiesAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => this.GetItemsAsync<CityModel>(this.GetCountryContainerName(), CountryContainerItemTypes.City, lifecycle, cancellationToken);

    public Task<IIdentityReferenceSnapshot<CityModel>> LoadCitiesSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => this.LoadSnapshotAsync<CityModel>(this.GetCountryContainerName(), CountryContainerItemTypes.City, lifecycle, cancellationToken);

    // ---- Companies -----------------------------------------------------------------------------------

    public Task<CompanyModel?> ResolveCompanyAsync(string id, CancellationToken cancellationToken = default)
        => this.GetItemByIdAsync<CompanyModel>(id, this.GetCompanyContainerName(), null, cancellationToken);

    public Task<CompanyModel?> ResolveCompanyAsync(long id, CancellationToken cancellationToken = default)
        => this.ResolveCompanyAsync(ToDocumentId(id), cancellationToken);

    public Task<IReadOnlyDictionary<string, CompanyModel>> GetCompaniesAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => this.GetItemsAsync<CompanyModel>(this.GetCompanyContainerName(), null, lifecycle, cancellationToken);

    public Task<IIdentityReferenceSnapshot<CompanyModel>> LoadCompaniesSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => this.LoadSnapshotAsync<CompanyModel>(this.GetCompanyContainerName(), null, lifecycle, cancellationToken);

    // ---- Company branches ----------------------------------------------------------------------------

    public Task<CompanyBranchModel?> ResolveCompanyBranchAsync(string id, CancellationToken cancellationToken = default)
        => this.GetItemByIdAsync<CompanyBranchModel>(id, this.GetCompanyBranchContainerName(), CompanyBranchContainerItemTypes.Branch, cancellationToken);

    public Task<CompanyBranchModel?> ResolveCompanyBranchAsync(long id, CancellationToken cancellationToken = default)
        => this.ResolveCompanyBranchAsync(ToDocumentId(id), cancellationToken);

    public Task<IReadOnlyDictionary<string, CompanyBranchModel>> GetCompanyBranchesAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => this.GetItemsAsync<CompanyBranchModel>(this.GetCompanyBranchContainerName(), CompanyBranchContainerItemTypes.Branch, lifecycle, cancellationToken);

    public Task<IIdentityReferenceSnapshot<CompanyBranchModel>> LoadCompanyBranchesSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => this.LoadSnapshotAsync<CompanyBranchModel>(this.GetCompanyBranchContainerName(), CompanyBranchContainerItemTypes.Branch, lifecycle, cancellationToken);

    // ---- Services ------------------------------------------------------------------------------------

    public Task<ServiceModel?> ResolveServiceAsync(string id, CancellationToken cancellationToken = default)
        => this.GetItemByIdAsync<ServiceModel>(id, this.GetServiceContainerName(), null, cancellationToken);

    public Task<ServiceModel?> ResolveServiceAsync(long id, CancellationToken cancellationToken = default)
        => this.ResolveServiceAsync(ToDocumentId(id), cancellationToken);

    public Task<IReadOnlyDictionary<string, ServiceModel>> GetServicesAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => this.GetItemsAsync<ServiceModel>(this.GetServiceContainerName(), null, lifecycle, cancellationToken);

    public Task<IIdentityReferenceSnapshot<ServiceModel>> LoadServicesSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => this.LoadSnapshotAsync<ServiceModel>(this.GetServiceContainerName(), null, lifecycle, cancellationToken);

    // ---- Departments ---------------------------------------------------------------------------------

    public Task<DepartmentModel?> ResolveDepartmentAsync(string id, CancellationToken cancellationToken = default)
        => this.GetItemByIdAsync<DepartmentModel>(id, this.GetDepartmentContainerName(), null, cancellationToken);

    public Task<DepartmentModel?> ResolveDepartmentAsync(long id, CancellationToken cancellationToken = default)
        => this.ResolveDepartmentAsync(ToDocumentId(id), cancellationToken);

    public Task<IReadOnlyDictionary<string, DepartmentModel>> GetDepartmentsAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => this.GetItemsAsync<DepartmentModel>(this.GetDepartmentContainerName(), null, lifecycle, cancellationToken);

    public Task<IIdentityReferenceSnapshot<DepartmentModel>> LoadDepartmentsSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => this.LoadSnapshotAsync<DepartmentModel>(this.GetDepartmentContainerName(), null, lifecycle, cancellationToken);

    // ---- Teams ---------------------------------------------------------------------------------------

    public Task<TeamModel?> ResolveTeamAsync(string id, CancellationToken cancellationToken = default)
        => this.GetItemByIdAsync<TeamModel>(id, this.GetTeamContainerName(), null, cancellationToken);

    public Task<TeamModel?> ResolveTeamAsync(long id, CancellationToken cancellationToken = default)
        => this.ResolveTeamAsync(ToDocumentId(id), cancellationToken);

    public Task<IReadOnlyDictionary<string, TeamModel>> GetTeamsAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => this.GetItemsAsync<TeamModel>(this.GetTeamContainerName(), null, lifecycle, cancellationToken);

    public Task<IIdentityReferenceSnapshot<TeamModel>> LoadTeamsSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => this.LoadSnapshotAsync<TeamModel>(this.GetTeamContainerName(), null, lifecycle, cancellationToken);

    // ---- Brands --------------------------------------------------------------------------------------

    public Task<BrandModel?> ResolveBrandAsync(string id, CancellationToken cancellationToken = default)
        => this.GetItemByIdAsync<BrandModel>(id, this.GetBrandContainerName(), null, cancellationToken);

    public Task<BrandModel?> ResolveBrandAsync(long id, CancellationToken cancellationToken = default)
        => this.ResolveBrandAsync(ToDocumentId(id), cancellationToken);

    public Task<IReadOnlyDictionary<string, BrandModel>> GetBrandsAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => this.GetItemsAsync<BrandModel>(this.GetBrandContainerName(), null, lifecycle, cancellationToken);

    public Task<IIdentityReferenceSnapshot<BrandModel>> LoadBrandsSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => this.LoadSnapshotAsync<BrandModel>(this.GetBrandContainerName(), null, lifecycle, cancellationToken);

    // ---- Users ---------------------------------------------------------------------------------------

    public Task<UserModel?> ResolveUserAsync(string id, CancellationToken cancellationToken = default)
        => this.GetItemByIdAsync<UserModel>(id, this.GetUserContainerName(), null, cancellationToken);

    public Task<UserModel?> ResolveUserAsync(long id, CancellationToken cancellationToken = default)
        => this.ResolveUserAsync(ToDocumentId(id), cancellationToken);

    public Task<IReadOnlyDictionary<string, UserModel>> GetUsersAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => this.GetItemsAsync<UserModel>(this.GetUserContainerName(), null, lifecycle, cancellationToken);

    public Task<IIdentityReferenceSnapshot<UserModel>> LoadUsersSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => this.LoadSnapshotAsync<UserModel>(this.GetUserContainerName(), null, lifecycle, cancellationToken);

    // ---- Refresh -------------------------------------------------------------------------------------

    public Task RefreshAsync(IdentityReferenceFamily family, CancellationToken cancellationToken = default)
        => family switch
        {
            IdentityReferenceFamily.Countries => this.RefreshFamilyAsync<CountryModel>(this.GetCountryContainerName(), CountryContainerItemTypes.Country, cancellationToken),
            IdentityReferenceFamily.Regions => this.RefreshFamilyAsync<RegionModel>(this.GetCountryContainerName(), CountryContainerItemTypes.Region, cancellationToken),
            IdentityReferenceFamily.Cities => this.RefreshFamilyAsync<CityModel>(this.GetCountryContainerName(), CountryContainerItemTypes.City, cancellationToken),
            IdentityReferenceFamily.Companies => this.RefreshFamilyAsync<CompanyModel>(this.GetCompanyContainerName(), null, cancellationToken),
            IdentityReferenceFamily.CompanyBranches => this.RefreshFamilyAsync<CompanyBranchModel>(this.GetCompanyBranchContainerName(), CompanyBranchContainerItemTypes.Branch, cancellationToken),
            IdentityReferenceFamily.Services => this.RefreshFamilyAsync<ServiceModel>(this.GetServiceContainerName(), null, cancellationToken),
            IdentityReferenceFamily.Departments => this.RefreshFamilyAsync<DepartmentModel>(this.GetDepartmentContainerName(), null, cancellationToken),
            IdentityReferenceFamily.Teams => this.RefreshFamilyAsync<TeamModel>(this.GetTeamContainerName(), null, cancellationToken),
            IdentityReferenceFamily.Brands => this.RefreshFamilyAsync<BrandModel>(this.GetBrandContainerName(), null, cancellationToken),
            IdentityReferenceFamily.Users => this.RefreshFamilyAsync<UserModel>(this.GetUserContainerName(), null, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown identity reference family."),
        };

    public Task RefreshAsync(IdentityReferenceFamily family, string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(id));

        return family switch
        {
            IdentityReferenceFamily.Countries => this.RefreshItemAsync<CountryModel>(id, this.GetCountryContainerName(), CountryContainerItemTypes.Country, cancellationToken),
            IdentityReferenceFamily.Regions => this.RefreshItemAsync<RegionModel>(id, this.GetCountryContainerName(), CountryContainerItemTypes.Region, cancellationToken),
            IdentityReferenceFamily.Cities => this.RefreshItemAsync<CityModel>(id, this.GetCountryContainerName(), CountryContainerItemTypes.City, cancellationToken),
            IdentityReferenceFamily.Companies => this.RefreshItemAsync<CompanyModel>(id, this.GetCompanyContainerName(), null, cancellationToken),
            IdentityReferenceFamily.CompanyBranches => this.RefreshItemAsync<CompanyBranchModel>(id, this.GetCompanyBranchContainerName(), CompanyBranchContainerItemTypes.Branch, cancellationToken),
            IdentityReferenceFamily.Services => this.RefreshItemAsync<ServiceModel>(id, this.GetServiceContainerName(), null, cancellationToken),
            IdentityReferenceFamily.Departments => this.RefreshItemAsync<DepartmentModel>(id, this.GetDepartmentContainerName(), null, cancellationToken),
            IdentityReferenceFamily.Teams => this.RefreshItemAsync<TeamModel>(id, this.GetTeamContainerName(), null, cancellationToken),
            IdentityReferenceFamily.Brands => this.RefreshItemAsync<BrandModel>(id, this.GetBrandContainerName(), null, cancellationToken),
            IdentityReferenceFamily.Users => this.RefreshItemAsync<UserModel>(id, this.GetUserContainerName(), null, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown identity reference family."),
        };
    }

    public Task RefreshAsync(IdentityReferenceFamily family, long id, CancellationToken cancellationToken = default)
        => this.RefreshAsync(family, ToDocumentId(id), cancellationToken);

    // ---- Reads ---------------------------------------------------------------------------------------

    /// <summary>
    /// Reads a whole family and applies <paramref name="lifecycle"/> to the result. The cache underneath
    /// holds every row; the filter is applied to a view derived from it, never to what is stored.
    ///
    /// <para>Deliberately not an <c>async</c> method. A remembered read returns the cached task exactly as
    /// it is, so a hit costs one dictionary lookup and no allocation at all — including no state machine,
    /// which is what an <c>async</c> keyword here would add to every call. The load lives in its own method
    /// for the same reason: a lambda that captures this one's arguments is built when the method is
    /// entered, not when the lambda is reached, so leaving it here would allocate a closure on every hit.</para>
    /// </summary>
    private Task<IReadOnlyDictionary<string, TModel>> GetItemsAsync<TModel>(
        string containerName,
        string? itemType,
        IdentityLifecycleFilter lifecycle,
        CancellationToken cancellationToken)
        where TModel : ReplicationModel
    {
        var family = this.FamilyKey<TModel>(containerName, itemType);

        return this.cache.TryGetRoster<TModel>(in family, lifecycle)
            ?? this.LoadItemsAsync<TModel>(family, containerName, itemType, lifecycle, cancellationToken);
    }

    private Task<IReadOnlyDictionary<string, TModel>> LoadItemsAsync<TModel>(
        IdentityReferenceFamilyKey family,
        string containerName,
        string? itemType,
        IdentityLifecycleFilter lifecycle,
        CancellationToken cancellationToken)
        where TModel : ReplicationModel
        => this.cache.GetOrLoadRosterAsync(
            family,
            lifecycle,
            token => this.LoadFamilyAsync<TModel>(containerName, itemType, token),
            ApplyLifecycleFilter,
            cancellationToken);

    /// <summary>
    /// Takes a pinned snapshot, read through to Cosmos rather than served from what is remembered — a run
    /// pins rows as of its own start, not as of whenever some earlier caller warmed them. What it reads is
    /// published to the cache on the way past, so the run does not leave the store having been scanned for
    /// nothing.
    /// </summary>
    private async Task<IIdentityReferenceSnapshot<TModel>> LoadSnapshotAsync<TModel>(
        string containerName,
        string? itemType,
        IdentityLifecycleFilter lifecycle,
        CancellationToken cancellationToken)
        where TModel : ReplicationModel
    {
        var superset = await this.LoadFamilyAsync<TModel>(containerName, itemType, cancellationToken).ConfigureAwait(false);
        var family = this.FamilyKey<TModel>(containerName, itemType);

        this.cache.PublishSuperset(in family, superset);

        var rows = ApplyLifecycleFilter(superset, lifecycle);

        // The snapshot takes its own copy, so it stays as it was even as the cache below it keeps filling.
        return new IdentityReferenceSnapshot<TModel>(rows, lifecycle);
    }

    /// <summary>
    /// Reads one row by id.
    ///
    /// <para><b>This does not filter on lifecycle, and must not be given a filter.</b> Everything that asks
    /// for a row by id is holding that id because something referenced it, and a reference written in the
    /// past stays valid after the company or branch it points at closes. A filter here would turn every
    /// historical reference to a closed company into a null, which downstream becomes a blank name in a
    /// rendered output — or worse, a dropped record, where a caller reads the missing name as a missing
    /// entity. The lifecycle state travels back on the model instead, where the caller can act on it.</para>
    ///
    /// <para>Reading the family's rows first is safe precisely because what is cached under a family is the
    /// unfiltered superset — the filtered views sit beside it and are never read here. If that ever stops
    /// being true, this read has to stop consulting them.</para>
    ///
    /// <para>Like the roster read, deliberately not <c>async</c>: a row that has been read before is handed
    /// back as the same completed task, allocating nothing.</para>
    /// </summary>
    private Task<TModel?> GetItemByIdAsync<TModel>(
        string id,
        string containerName,
        string? itemType,
        CancellationToken cancellationToken)
        where TModel : ReplicationModel
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(id));

        var item = this.ItemKey<TModel>(containerName, itemType, id);

        if (this.cache.TryGetItem<TModel>(in item) is { } remembered)
            return remembered;

        var family = this.FamilyKey<TModel>(containerName, itemType);

        // The whole family, if it happens to be loaded already — the unfiltered superset, never a view. A
        // load still in flight is left alone: waiting on someone else's full-family scan is exactly what a
        // single-row read behind a short timeout must not do.
        if (this.cache.TryGetSuperset<TModel>(in family) is { IsCompletedSuccessfully: true } superset
            && superset.Result.TryGetValue(id, out var row))
        {
            return this.cache.PublishItem(in item, row);
        }

        return this.LoadItemAsync<TModel>(item, containerName, itemType, id, cancellationToken);
    }

    /// <summary>
    /// The read behind <see cref="GetItemByIdAsync"/>, kept in its own method so the closure it needs is
    /// built only when the store is actually going to be touched.
    /// </summary>
    private Task<TModel?> LoadItemAsync<TModel>(
        IdentityReferenceItemKey item,
        string containerName,
        string? itemType,
        string id,
        CancellationToken cancellationToken)
        where TModel : ReplicationModel
        => this.cache.GetOrLoadItemAsync(
            item,
            token => this.ReadRowAsync<TModel>(containerName, itemType, id, token),
            cancellationToken);

    private async Task RefreshFamilyAsync<TModel>(string containerName, string? itemType, CancellationToken cancellationToken)
        where TModel : ReplicationModel
    {
        var superset = await this.LoadFamilyAsync<TModel>(containerName, itemType, cancellationToken).ConfigureAwait(false);
        var family = this.FamilyKey<TModel>(containerName, itemType);

        this.cache.PublishSuperset(in family, superset);
    }

    private async Task RefreshItemAsync<TModel>(string id, string containerName, string? itemType, CancellationToken cancellationToken)
        where TModel : ReplicationModel
    {
        var row = await this.ReadRowAsync<TModel>(containerName, itemType, id, cancellationToken).ConfigureAwait(false);

        var family = this.FamilyKey<TModel>(containerName, itemType);
        var item = this.ItemKey<TModel>(containerName, itemType, id);

        // Both, and in this order: the roster has to agree with the row, or one refreshed row would be
        // right when resolved and wrong when listed.
        this.cache.ReplaceInSuperset(in family, id, row);

        // The task it hands back is for callers reading a row; here the point is only that it is stored.
        _ = this.cache.PublishItem(in item, row);
    }

    private async Task<IReadOnlyDictionary<string, TModel>> LoadFamilyAsync<TModel>(
        string containerName,
        string? itemType,
        CancellationToken cancellationToken)
        where TModel : ReplicationModel
    {
        var rows = await this.ReadRowsAsync<TModel>(containerName, itemType, null, cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<string, TModel>(rows.Count, StringComparer.Ordinal);

        foreach (var row in rows)
            result[ResolveIdValue(row)] = row;

        // Wrapped once, here, rather than copied per call: what goes into the cache is handed to every
        // later caller by reference, and with a process-wide cache that makes it shared state. Read-only is
        // what lets it be shared safely, and it costs one object per family rather than one per read.
        return new ReadOnlyDictionary<string, TModel>(result);
    }

    // ---- Cosmos --------------------------------------------------------------------------------------

    /// <summary>
    /// Reads rows out of Cosmos: a whole family when <paramref name="id"/> is null, otherwise the one row
    /// with that id. Every page is followed to the end — a first page can come back empty with the match on
    /// a later one, which is how a document that exists comes back as a null.
    ///
    /// <para>The single place this type touches the store, so that everything above it is testable without
    /// one.</para>
    /// </summary>
    internal virtual async Task<IReadOnlyList<TModel>> ReadRowsAsync<TModel>(
        string containerName,
        string? itemType,
        string? id,
        CancellationToken cancellationToken)
        where TModel : ReplicationModel
    {
        var container = this.cosmosClient.GetContainer(this.GetDatabaseName(), containerName);
        var requestOptions = id is null ? null : new QueryRequestOptions { MaxItemCount = 1 };
        var iterator = container.GetItemQueryIterator<TModel>(BuildQueryDefinition(itemType, id), requestOptions: requestOptions);
        var rows = new List<TModel>();

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);

            rows.AddRange(page);

            if (id is not null && rows.Count > 0)
                break;
        }

        return rows;
    }

    private async Task<TModel?> ReadRowAsync<TModel>(string containerName, string? itemType, string id, CancellationToken cancellationToken)
        where TModel : ReplicationModel
        => (await this.ReadRowsAsync<TModel>(containerName, itemType, id, cancellationToken).ConfigureAwait(false)).FirstOrDefault();

    // ---- Lifecycle -----------------------------------------------------------------------------------

    /// <summary>
    /// Derives a filtered view of a family. <see cref="IdentityLifecycleFilter.All"/> is handed back the
    /// superset itself rather than a copy of it, which is why the commonest roster read costs nothing.
    /// </summary>
    private static IReadOnlyDictionary<string, TModel> ApplyLifecycleFilter<TModel>(
        IReadOnlyDictionary<string, TModel> source,
        IdentityLifecycleFilter lifecycle)
        where TModel : ReplicationModel
    {
        if (lifecycle is not (IdentityLifecycleFilter.ExcludeDeleted or IdentityLifecycleFilter.ActiveOnly))
            return source;

        var result = new Dictionary<string, TModel>(StringComparer.Ordinal);

        foreach (var entry in source)
        {
            if (MatchesLifecycle(entry.Value, lifecycle))
                result[entry.Key] = entry.Value;
        }

        return new ReadOnlyDictionary<string, TModel>(result);
    }

    private static bool MatchesLifecycle<TModel>(TModel item, IdentityLifecycleFilter lifecycle)
        where TModel : ReplicationModel
        => lifecycle switch
        {
            IdentityLifecycleFilter.ExcludeDeleted => !item.IsDeleted,
            IdentityLifecycleFilter.ActiveOnly => !item.IsDeleted && !IsTerminated(item),

            // All, and any value this build does not recognise. Failing open keeps an unknown filter from
            // silently dropping rows; over-listing is visible, under-listing is not.
            _ => true,
        };

    /// <summary>
    /// Terminated means the closure date has passed. Only companies and branches carry one — nothing else
    /// in identity has a notion of being closed but still referenced — so every other family is never
    /// terminated, and <see cref="IdentityLifecycleFilter.ActiveOnly"/> and
    /// <see cref="IdentityLifecycleFilter.ExcludeDeleted"/> mean the same thing for them.
    /// </summary>
    private static bool IsTerminated<TModel>(TModel item)
        where TModel : ReplicationModel
        => item switch
        {
            CompanyModel company => company.TerminationDate is not null && company.TerminationDate <= DateTime.UtcNow,
            CompanyBranchModel branch => branch.TerminationDate is not null && branch.TerminationDate <= DateTime.UtcNow,
            _ => false,
        };

    // ---- Plumbing ------------------------------------------------------------------------------------

    /// <summary>
    /// Numeric ids and document ids are the same value in two shapes — the replication mapper writes the
    /// document id as <c>ID.ToString()</c>. Invariant culture, always: a host running under a culture with
    /// its own digit shapes has to key the same documents as one running under en-US.
    /// </summary>
    private static string ToDocumentId(long id) => id.ToString(CultureInfo.InvariantCulture);

    private static QueryDefinition BuildQueryDefinition(string? itemType, string? id)
    {
        if (string.IsNullOrWhiteSpace(itemType) && string.IsNullOrWhiteSpace(id))
            return new QueryDefinition("SELECT * FROM c");

        if (string.IsNullOrWhiteSpace(itemType))
            return new QueryDefinition("SELECT * FROM c WHERE c.id = @id")
                .WithParameter("@id", id);

        if (string.IsNullOrWhiteSpace(id))
            return new QueryDefinition("SELECT * FROM c WHERE c.ItemType = @itemType")
                .WithParameter("@itemType", itemType);

        return new QueryDefinition("SELECT * FROM c WHERE c.id = @id AND c.ItemType = @itemType")
            .WithParameter("@id", id)
            .WithParameter("@itemType", itemType);
    }

    private IdentityReferenceFamilyKey FamilyKey<TModel>(string containerName, string? itemType)
        => new(typeof(TModel), this.GetDatabaseName(), containerName, itemType);

    private IdentityReferenceItemKey ItemKey<TModel>(string containerName, string? itemType, string id)
        => new(typeof(TModel), this.GetDatabaseName(), containerName, itemType, id);

    private static string ResolveIdValue<TModel>(TModel item)
        where TModel : ReplicationModel
    {
        var resolver = IdValueResolvers.GetOrAdd(typeof(TModel), type =>
        {
            var property = type.GetProperty("id", BindingFlags.Instance | BindingFlags.Public)
                ?? type.GetProperty("ID", BindingFlags.Instance | BindingFlags.Public)
                ?? type.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public);

            if (property is null)
                throw new InvalidOperationException($"Type '{type.FullName}' must include a public id property.");

            return instance => property.GetValue(instance)?.ToString()
                ?? throw new InvalidOperationException($"Type '{type.FullName}' has a null id value.");
        });

        return resolver(item);
    }

    private string GetDatabaseName() => this.options.DatabaseName;

    private string GetCountryContainerName() => this.options.CountryContainerName;

    private string GetCompanyContainerName() => this.options.CompanyContainerName;

    private string GetCompanyBranchContainerName() => this.options.CompanyBranchContainerName;

    private string GetServiceContainerName() => this.options.ServiceContainerName;

    private string GetDepartmentContainerName() => this.options.DepartmentContainerName;

    private string GetTeamContainerName() => this.options.TeamContainerName;

    private string GetBrandContainerName() => this.options.BrandContainerName;

    private string GetUserContainerName() => this.options.UserContainerName;
}
