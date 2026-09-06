using System.Collections.Concurrent;
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
/// <para>Generic over <typeparamref name="TCosmosClient"/> so a host that keeps more than one client can
/// say which to read identity through.</para>
/// </summary>
public class CosmosIdentityReferenceSource<TCosmosClient>(
    TCosmosClient cosmosClient,
    IOptions<CosmosIdentityReferenceOptions> options) : IIdentityReferenceSource
    where TCosmosClient : CosmosClient
{
    private static readonly ConcurrentDictionary<Type, Func<object, string>> IdValueResolvers = new();
    private static readonly object ItemNotFound = new();

    private readonly CosmosIdentityReferenceOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, object> _listCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, object> _itemCache = new(StringComparer.Ordinal);

    // ---- Countries -----------------------------------------------------------------------------------

    public Task<CountryModel?> ResolveCountryAsync(string id, CancellationToken cancellationToken = default)
        => GetItemByIdAsync<CountryModel>(id, GetDatabaseName(), GetCountryContainerName(), CountryContainerItemTypes.Country, cancellationToken);

    public Task<CountryModel?> ResolveCountryAsync(long id, CancellationToken cancellationToken = default)
        => ResolveCountryAsync(ToDocumentId(id), cancellationToken);

    public Task<IReadOnlyDictionary<string, CountryModel>> GetCountriesAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => GetItemsAsync<CountryModel>(GetDatabaseName(), GetCountryContainerName(), CountryContainerItemTypes.Country, lifecycle, cancellationToken);

    public Task<IIdentityReferenceSnapshot<CountryModel>> LoadCountriesSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => LoadSnapshotAsync<CountryModel>(GetDatabaseName(), GetCountryContainerName(), CountryContainerItemTypes.Country, lifecycle, cancellationToken);

    // ---- Regions -------------------------------------------------------------------------------------

    public Task<RegionModel?> ResolveRegionAsync(string id, CancellationToken cancellationToken = default)
        => GetItemByIdAsync<RegionModel>(id, GetDatabaseName(), GetCountryContainerName(), CountryContainerItemTypes.Region, cancellationToken);

    public Task<RegionModel?> ResolveRegionAsync(long id, CancellationToken cancellationToken = default)
        => ResolveRegionAsync(ToDocumentId(id), cancellationToken);

    public Task<IReadOnlyDictionary<string, RegionModel>> GetRegionsAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => GetItemsAsync<RegionModel>(GetDatabaseName(), GetCountryContainerName(), CountryContainerItemTypes.Region, lifecycle, cancellationToken);

    public Task<IIdentityReferenceSnapshot<RegionModel>> LoadRegionsSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => LoadSnapshotAsync<RegionModel>(GetDatabaseName(), GetCountryContainerName(), CountryContainerItemTypes.Region, lifecycle, cancellationToken);

    // ---- Cities --------------------------------------------------------------------------------------

    public Task<CityModel?> ResolveCityAsync(string id, CancellationToken cancellationToken = default)
        => GetItemByIdAsync<CityModel>(id, GetDatabaseName(), GetCountryContainerName(), CountryContainerItemTypes.City, cancellationToken);

    public Task<CityModel?> ResolveCityAsync(long id, CancellationToken cancellationToken = default)
        => ResolveCityAsync(ToDocumentId(id), cancellationToken);

    public Task<IReadOnlyDictionary<string, CityModel>> GetCitiesAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => GetItemsAsync<CityModel>(GetDatabaseName(), GetCountryContainerName(), CountryContainerItemTypes.City, lifecycle, cancellationToken);

    public Task<IIdentityReferenceSnapshot<CityModel>> LoadCitiesSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => LoadSnapshotAsync<CityModel>(GetDatabaseName(), GetCountryContainerName(), CountryContainerItemTypes.City, lifecycle, cancellationToken);

    // ---- Companies -----------------------------------------------------------------------------------

    public Task<CompanyModel?> ResolveCompanyAsync(string id, CancellationToken cancellationToken = default)
        => GetItemByIdAsync<CompanyModel>(id, GetDatabaseName(), GetCompanyContainerName(), null, cancellationToken);

    public Task<CompanyModel?> ResolveCompanyAsync(long id, CancellationToken cancellationToken = default)
        => ResolveCompanyAsync(ToDocumentId(id), cancellationToken);

    public Task<IReadOnlyDictionary<string, CompanyModel>> GetCompaniesAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => GetItemsAsync<CompanyModel>(GetDatabaseName(), GetCompanyContainerName(), null, lifecycle, cancellationToken);

    public Task<IIdentityReferenceSnapshot<CompanyModel>> LoadCompaniesSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => LoadSnapshotAsync<CompanyModel>(GetDatabaseName(), GetCompanyContainerName(), null, lifecycle, cancellationToken);

    // ---- Company branches ----------------------------------------------------------------------------

    public Task<CompanyBranchModel?> ResolveCompanyBranchAsync(string id, CancellationToken cancellationToken = default)
        => GetItemByIdAsync<CompanyBranchModel>(id, GetDatabaseName(), GetCompanyBranchContainerName(), CompanyBranchContainerItemTypes.Branch, cancellationToken);

    public Task<CompanyBranchModel?> ResolveCompanyBranchAsync(long id, CancellationToken cancellationToken = default)
        => ResolveCompanyBranchAsync(ToDocumentId(id), cancellationToken);

    public Task<IReadOnlyDictionary<string, CompanyBranchModel>> GetCompanyBranchesAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => GetItemsAsync<CompanyBranchModel>(GetDatabaseName(), GetCompanyBranchContainerName(), CompanyBranchContainerItemTypes.Branch, lifecycle, cancellationToken);

    public Task<IIdentityReferenceSnapshot<CompanyBranchModel>> LoadCompanyBranchesSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => LoadSnapshotAsync<CompanyBranchModel>(GetDatabaseName(), GetCompanyBranchContainerName(), CompanyBranchContainerItemTypes.Branch, lifecycle, cancellationToken);

    // ---- Services ------------------------------------------------------------------------------------

    public Task<ServiceModel?> ResolveServiceAsync(string id, CancellationToken cancellationToken = default)
        => GetItemByIdAsync<ServiceModel>(id, GetDatabaseName(), GetServiceContainerName(), null, cancellationToken);

    public Task<ServiceModel?> ResolveServiceAsync(long id, CancellationToken cancellationToken = default)
        => ResolveServiceAsync(ToDocumentId(id), cancellationToken);

    public Task<IReadOnlyDictionary<string, ServiceModel>> GetServicesAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => GetItemsAsync<ServiceModel>(GetDatabaseName(), GetServiceContainerName(), null, lifecycle, cancellationToken);

    public Task<IIdentityReferenceSnapshot<ServiceModel>> LoadServicesSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => LoadSnapshotAsync<ServiceModel>(GetDatabaseName(), GetServiceContainerName(), null, lifecycle, cancellationToken);

    // ---- Departments ---------------------------------------------------------------------------------

    public Task<DepartmentModel?> ResolveDepartmentAsync(string id, CancellationToken cancellationToken = default)
        => GetItemByIdAsync<DepartmentModel>(id, GetDatabaseName(), GetDepartmentContainerName(), null, cancellationToken);

    public Task<DepartmentModel?> ResolveDepartmentAsync(long id, CancellationToken cancellationToken = default)
        => ResolveDepartmentAsync(ToDocumentId(id), cancellationToken);

    public Task<IReadOnlyDictionary<string, DepartmentModel>> GetDepartmentsAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => GetItemsAsync<DepartmentModel>(GetDatabaseName(), GetDepartmentContainerName(), null, lifecycle, cancellationToken);

    public Task<IIdentityReferenceSnapshot<DepartmentModel>> LoadDepartmentsSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => LoadSnapshotAsync<DepartmentModel>(GetDatabaseName(), GetDepartmentContainerName(), null, lifecycle, cancellationToken);

    // ---- Teams ---------------------------------------------------------------------------------------

    public Task<TeamModel?> ResolveTeamAsync(string id, CancellationToken cancellationToken = default)
        => GetItemByIdAsync<TeamModel>(id, GetDatabaseName(), GetTeamContainerName(), null, cancellationToken);

    public Task<TeamModel?> ResolveTeamAsync(long id, CancellationToken cancellationToken = default)
        => ResolveTeamAsync(ToDocumentId(id), cancellationToken);

    public Task<IReadOnlyDictionary<string, TeamModel>> GetTeamsAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => GetItemsAsync<TeamModel>(GetDatabaseName(), GetTeamContainerName(), null, lifecycle, cancellationToken);

    public Task<IIdentityReferenceSnapshot<TeamModel>> LoadTeamsSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => LoadSnapshotAsync<TeamModel>(GetDatabaseName(), GetTeamContainerName(), null, lifecycle, cancellationToken);

    // ---- Brands --------------------------------------------------------------------------------------

    public Task<BrandModel?> ResolveBrandAsync(string id, CancellationToken cancellationToken = default)
        => GetItemByIdAsync<BrandModel>(id, GetDatabaseName(), GetBrandContainerName(), null, cancellationToken);

    public Task<BrandModel?> ResolveBrandAsync(long id, CancellationToken cancellationToken = default)
        => ResolveBrandAsync(ToDocumentId(id), cancellationToken);

    public Task<IReadOnlyDictionary<string, BrandModel>> GetBrandsAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => GetItemsAsync<BrandModel>(GetDatabaseName(), GetBrandContainerName(), null, lifecycle, cancellationToken);

    public Task<IIdentityReferenceSnapshot<BrandModel>> LoadBrandsSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => LoadSnapshotAsync<BrandModel>(GetDatabaseName(), GetBrandContainerName(), null, lifecycle, cancellationToken);

    // ---- Users ---------------------------------------------------------------------------------------

    public Task<UserModel?> ResolveUserAsync(string id, CancellationToken cancellationToken = default)
        => GetItemByIdAsync<UserModel>(id, GetDatabaseName(), GetUserContainerName(), null, cancellationToken);

    public Task<UserModel?> ResolveUserAsync(long id, CancellationToken cancellationToken = default)
        => ResolveUserAsync(ToDocumentId(id), cancellationToken);

    public Task<IReadOnlyDictionary<string, UserModel>> GetUsersAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => GetItemsAsync<UserModel>(GetDatabaseName(), GetUserContainerName(), null, lifecycle, cancellationToken);

    public Task<IIdentityReferenceSnapshot<UserModel>> LoadUsersSnapshotAsync(IdentityLifecycleFilter lifecycle = IdentityLifecycleFilter.All, CancellationToken cancellationToken = default)
        => LoadSnapshotAsync<UserModel>(GetDatabaseName(), GetUserContainerName(), null, lifecycle, cancellationToken);

    // ---- Reads ---------------------------------------------------------------------------------------

    /// <summary>
    /// Reads a whole family and applies <paramref name="lifecycle"/> to the result. The cache underneath
    /// holds every row; the filter is applied to the copy that goes back to the caller, never to what is
    /// stored.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, TModel>> GetItemsAsync<TModel>(
        string databaseName,
        string containerName,
        string? itemType,
        IdentityLifecycleFilter lifecycle,
        CancellationToken cancellationToken)
        where TModel : ReplicationModel
    {
        var all = await GetUnfilteredItemsAsync<TModel>(databaseName, containerName, itemType, cancellationToken);

        return ApplyLifecycleFilter(all, lifecycle);
    }

    private async Task<IIdentityReferenceSnapshot<TModel>> LoadSnapshotAsync<TModel>(
        string databaseName,
        string containerName,
        string? itemType,
        IdentityLifecycleFilter lifecycle,
        CancellationToken cancellationToken)
        where TModel : ReplicationModel
    {
        var items = await GetItemsAsync<TModel>(databaseName, containerName, itemType, lifecycle, cancellationToken);

        // The snapshot takes its own copy, so it stays as it was even as the cache below it keeps filling.
        return new IdentityReferenceSnapshot<TModel>(items, lifecycle);
    }

    /// <summary>
    /// Loads and caches every row of a family, in whatever lifecycle state. This is the superset every
    /// filtered read is served from, and the reason a filtered read cannot poison the cache for anyone else.
    /// </summary>
    private async Task<ConcurrentDictionary<string, TModel>> GetUnfilteredItemsAsync<TModel>(
        string databaseName,
        string containerName,
        string? itemType,
        CancellationToken cancellationToken)
        where TModel : ReplicationModel
    {
        var listCacheKey = GetListCacheKey<TModel>(databaseName, containerName, itemType);
        if (_listCache.TryGetValue(listCacheKey, out var cached) && cached is ConcurrentDictionary<string, TModel> cachedResult)
            return cachedResult;

        var container = cosmosClient.GetContainer(databaseName, containerName);
        var queryDefinition = BuildQueryDefinition(itemType, null);
        var iterator = container.GetItemQueryIterator<TModel>(queryDefinition);

        var result = new ConcurrentDictionary<string, TModel>(StringComparer.Ordinal);

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            foreach (var item in page)
            {
                var id = ResolveIdValue(item);
                result[id] = item;
                _itemCache[GetItemCacheKey<TModel>(databaseName, containerName, itemType, id)] = item;
            }
        }

        _listCache[listCacheKey] = result;
        return result;
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
    /// <para>Reading the list cache first is safe precisely because that cache holds the unfiltered
    /// superset. If it ever starts holding a filtered set, this read has to stop using it.</para>
    /// </summary>
    private async Task<TModel?> GetItemByIdAsync<TModel>(
        string id,
        string databaseName,
        string containerName,
        string? itemType,
        CancellationToken cancellationToken)
        where TModel : ReplicationModel
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(id));

        var listCacheKey = GetListCacheKey<TModel>(databaseName, containerName, itemType);
        if (_listCache.TryGetValue(listCacheKey, out var listCacheObject) && listCacheObject is ConcurrentDictionary<string, TModel> listCache && listCache.TryGetValue(id, out var cachedFromList))
            return cachedFromList;

        var itemCacheKey = GetItemCacheKey<TModel>(databaseName, containerName, itemType, id);
        if (_itemCache.TryGetValue(itemCacheKey, out var cachedItem))
            return ReferenceEquals(cachedItem, ItemNotFound) ? null : cachedItem as TModel;

        var container = cosmosClient.GetContainer(databaseName, containerName);
        var queryDefinition = BuildQueryDefinition(itemType, id);
        var iterator = container.GetItemQueryIterator<TModel>(queryDefinition, requestOptions: new QueryRequestOptions { MaxItemCount = 1 });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            var model = page.FirstOrDefault();
            if (model is null)
                continue;

            _itemCache[itemCacheKey] = model;
            if (_listCache.TryGetValue(listCacheKey, out listCacheObject) && listCacheObject is ConcurrentDictionary<string, TModel> updatedList)
                updatedList[id] = model;

            return model;
        }

        _itemCache[itemCacheKey] = ItemNotFound;
        return null;
    }

    // ---- Lifecycle -----------------------------------------------------------------------------------

    private static Dictionary<string, TModel> ApplyLifecycleFilter<TModel>(
        IEnumerable<KeyValuePair<string, TModel>> source,
        IdentityLifecycleFilter lifecycle)
        where TModel : ReplicationModel
    {
        var result = new Dictionary<string, TModel>(StringComparer.Ordinal);

        foreach (var entry in source)
        {
            if (MatchesLifecycle(entry.Value, lifecycle))
                result[entry.Key] = entry.Value;
        }

        return result;
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

    private static string GetListCacheKey<TModel>(string databaseName, string containerName, string? itemType)
        => $"list::{typeof(TModel).FullName}::{databaseName}::{containerName}::{itemType ?? ""}";

    private static string GetItemCacheKey<TModel>(string databaseName, string containerName, string? itemType, string id)
        => $"item::{typeof(TModel).FullName}::{databaseName}::{containerName}::{itemType ?? ""}::{id}";

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

    private string GetDatabaseName() => _options.DatabaseName;

    private string GetCountryContainerName() => _options.CountryContainerName;

    private string GetCompanyContainerName() => _options.CompanyContainerName;

    private string GetCompanyBranchContainerName() => _options.CompanyBranchContainerName;

    private string GetServiceContainerName() => _options.ServiceContainerName;

    private string GetDepartmentContainerName() => _options.DepartmentContainerName;

    private string GetTeamContainerName() => _options.TeamContainerName;

    private string GetBrandContainerName() => _options.BrandContainerName;

    private string GetUserContainerName() => _options.UserContainerName;
}
