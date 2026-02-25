using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Data.Cosmos.Options;

namespace ShiftSoftware.ShiftIdentity.Data.Cosmos.Services;

public class IdentityReferenceCosmosDataService<TCosmosClient>(
    TCosmosClient cosmosClient,
    IOptions<IdentityReferenceCosmosDataOptions> options) : IIdentityReferenceCosmosDataService
    where TCosmosClient : CosmosClient
{
    private static readonly ConcurrentDictionary<Type, Func<object, string>> IdValueResolvers = new();
    private static readonly object ItemNotFound = new();

    private readonly IdentityReferenceCosmosDataOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, object> _listCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, object> _itemCache = new(StringComparer.Ordinal);

    public Task<Dictionary<string, CountryModel>> GetCountriesAsync(CancellationToken cancellationToken = default)
        => GetItemsAsync<CountryModel>(GetDatabaseName(), GetCountryContainerName(), CountryContainerItemTypes.Country, cancellationToken);

    public Task<CountryModel?> GetCountryByIdAsync(string id, CancellationToken cancellationToken = default)
        => GetItemByIdAsync<CountryModel>(id, GetDatabaseName(), GetCountryContainerName(), CountryContainerItemTypes.Country, cancellationToken);

    public Task<Dictionary<string, RegionModel>> GetRegionsAsync(CancellationToken cancellationToken = default)
        => GetItemsAsync<RegionModel>(GetDatabaseName(), GetCountryContainerName(), CountryContainerItemTypes.Region, cancellationToken);

    public Task<RegionModel?> GetRegionByIdAsync(string id, CancellationToken cancellationToken = default)
        => GetItemByIdAsync<RegionModel>(id, GetDatabaseName(), GetCountryContainerName(), CountryContainerItemTypes.Region, cancellationToken);

    public Task<Dictionary<string, CityModel>> GetCitiesAsync(CancellationToken cancellationToken = default)
        => GetItemsAsync<CityModel>(GetDatabaseName(), GetCountryContainerName(), CountryContainerItemTypes.City, cancellationToken);

    public Task<CityModel?> GetCityByIdAsync(string id, CancellationToken cancellationToken = default)
        => GetItemByIdAsync<CityModel>(id, GetDatabaseName(), GetCountryContainerName(), CountryContainerItemTypes.City, cancellationToken);

    public Task<Dictionary<string, CompanyModel>> GetCompaniesAsync(CancellationToken cancellationToken = default)
        => GetItemsAsync<CompanyModel>(GetDatabaseName(), GetCompanyContainerName(), null, cancellationToken);

    public Task<CompanyModel?> GetCompanyByIdAsync(string id, CancellationToken cancellationToken = default)
        => GetItemByIdAsync<CompanyModel>(id, GetDatabaseName(), GetCompanyContainerName(), null, cancellationToken);

    public Task<Dictionary<string, CompanyBranchModel>> GetCompanyBranchesAsync(CancellationToken cancellationToken = default)
        => GetItemsAsync<CompanyBranchModel>(GetDatabaseName(), GetCompanyBranchContainerName(), CompanyBranchContainerItemTypes.Branch, cancellationToken);

    public Task<CompanyBranchModel?> GetCompanyBranchByIdAsync(string id, CancellationToken cancellationToken = default)
        => GetItemByIdAsync<CompanyBranchModel>(id, GetDatabaseName(), GetCompanyBranchContainerName(), CompanyBranchContainerItemTypes.Branch, cancellationToken);

    public Task<Dictionary<string, ServiceModel>> GetServicesAsync(CancellationToken cancellationToken = default)
        => GetItemsAsync<ServiceModel>(GetDatabaseName(), GetServiceContainerName(), null, cancellationToken);

    public Task<ServiceModel?> GetServiceByIdAsync(string id, CancellationToken cancellationToken = default)
        => GetItemByIdAsync<ServiceModel>(id, GetDatabaseName(), GetServiceContainerName(), null, cancellationToken);

    public Task<Dictionary<string, DepartmentModel>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
        => GetItemsAsync<DepartmentModel>(GetDatabaseName(), GetDepartmentContainerName(), null, cancellationToken);

    public Task<DepartmentModel?> GetDepartmentByIdAsync(string id, CancellationToken cancellationToken = default)
        => GetItemByIdAsync<DepartmentModel>(id, GetDatabaseName(), GetDepartmentContainerName(), null, cancellationToken);

    public Task<Dictionary<string, TeamModel>> GetTeamsAsync(CancellationToken cancellationToken = default)
        => GetItemsAsync<TeamModel>(GetDatabaseName(), GetTeamContainerName(), null, cancellationToken);

    public Task<TeamModel?> GetTeamByIdAsync(string id, CancellationToken cancellationToken = default)
        => GetItemByIdAsync<TeamModel>(id, GetDatabaseName(), GetTeamContainerName(), null, cancellationToken);

    public Task<Dictionary<string, BrandModel>> GetBrandsAsync(CancellationToken cancellationToken = default)
        => GetItemsAsync<BrandModel>(GetDatabaseName(), GetBrandContainerName(), null, cancellationToken);

    public Task<BrandModel?> GetBrandByIdAsync(string id, CancellationToken cancellationToken = default)
        => GetItemByIdAsync<BrandModel>(id, GetDatabaseName(), GetBrandContainerName(), null, cancellationToken);

    private async Task<Dictionary<string, TModel>> GetItemsAsync<TModel>(
        string databaseName,
        string containerName,
        string? itemType,
        CancellationToken cancellationToken)
        where TModel : class
    {
        var listCacheKey = GetListCacheKey<TModel>(databaseName, containerName, itemType);
        if (_listCache.TryGetValue(listCacheKey, out var cached) && cached is ConcurrentDictionary<string, TModel> cachedResult)
            return new Dictionary<string, TModel>(cachedResult, StringComparer.Ordinal);

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
        return new Dictionary<string, TModel>(result, StringComparer.Ordinal);
    }

    private async Task<TModel?> GetItemByIdAsync<TModel>(
        string id,
        string databaseName,
        string containerName,
        string? itemType,
        CancellationToken cancellationToken)
        where TModel : class
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
        where TModel : class
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
}
