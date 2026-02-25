using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;

namespace ShiftSoftware.ShiftIdentity.Data.Cosmos.Services;

public interface IIdentityReferenceCosmosDataService
{
    Task<Dictionary<string, CountryModel>> GetCountriesAsync(CancellationToken cancellationToken = default);
    Task<CountryModel?> GetCountryByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<Dictionary<string, RegionModel>> GetRegionsAsync(CancellationToken cancellationToken = default);
    Task<RegionModel?> GetRegionByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<Dictionary<string, CityModel>> GetCitiesAsync(CancellationToken cancellationToken = default);
    Task<CityModel?> GetCityByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<Dictionary<string, CompanyModel>> GetCompaniesAsync(CancellationToken cancellationToken = default);
    Task<CompanyModel?> GetCompanyByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<Dictionary<string, CompanyBranchModel>> GetCompanyBranchesAsync(CancellationToken cancellationToken = default);
    Task<CompanyBranchModel?> GetCompanyBranchByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<Dictionary<string, ServiceModel>> GetServicesAsync(CancellationToken cancellationToken = default);
    Task<ServiceModel?> GetServiceByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<Dictionary<string, DepartmentModel>> GetDepartmentsAsync(CancellationToken cancellationToken = default);
    Task<DepartmentModel?> GetDepartmentByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<Dictionary<string, TeamModel>> GetTeamsAsync(CancellationToken cancellationToken = default);
    Task<TeamModel?> GetTeamByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<Dictionary<string, BrandModel>> GetBrandsAsync(CancellationToken cancellationToken = default);
    Task<BrandModel?> GetBrandByIdAsync(string id, CancellationToken cancellationToken = default);
}
