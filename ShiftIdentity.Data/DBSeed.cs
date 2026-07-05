using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Model.Enums;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using System.Text.Json;

namespace ShiftSoftware.ShiftIdentity.Data;

public class DBSeed
{
    private ShiftIdentityDbContext db;
    private DBSeedOptions? dbSeedOptions;

    private List<Type> actionTrees;
    private readonly string adminUserName;
    private readonly string adminPassword;

    public DBSeed(ShiftIdentityDbContext db, List<Type> actionTrees, string adminUserName, string adminPassword, DBSeedOptions? dBSeedOptions)
    {
        this.db = db;
        this.actionTrees = actionTrees;
        this.adminUserName = adminUserName;
        this.adminPassword = adminPassword;
        dbSeedOptions = dBSeedOptions;
    }

    public async Task SeedAsync()
    {
        Country country = await SeedCountryAsync();

        Region region = await SeedRegionAsync(country);

        City city = await SeedCityAsync(region);

        Company company = await SeedCompanyAsync();

        CompanyBranch companyBranch = await SeedCompanyBranchAsync(city, company);

        await SeedUserAsync(country, region, company, companyBranch);

        await db.SaveChangesAsync();
    }

    private async Task<Country> SeedCountryAsync()
    {
        var country = await db.Countries.FirstOrDefaultAsync(x => x.BuiltIn);
        if (country == null) {
            country = new();
        }

        var lang = dbSeedOptions?.DefaultCulture.TwoLetterISOLanguageName;
        country.Name = BuildLocalizedText(lang ?? "en", Constants.BuiltInCountry);
        country.ShortCode = dbSeedOptions?.CountryShortCode;
        country.IntegrationId = dbSeedOptions?.CountryExternalId;
        country.CallingCode = dbSeedOptions?.CountryCallingCode ?? Core.Constants.BuiltInCountryCallingCode;
        country.BuiltIn = true;

        return country;
    }

    private async Task<Region> SeedRegionAsync(Country country)
    {
        var region = await db.Regions.FirstOrDefaultAsync(x => x.BuiltIn);

        if (region == null)
            region = new();

        var lang = dbSeedOptions?.DefaultCulture.TwoLetterISOLanguageName;
        region.Name = BuildLocalizedText(lang ?? "en", Constants.BuiltInRegion);
        region.ShortCode = dbSeedOptions?.RegionShortCode;
        region.IntegrationId = dbSeedOptions?.RegionExternalId;
        region.BuiltIn = true;
        region.Country = country;

        return region;
    }

    private async Task<City> SeedCityAsync(Region region)
    {
        var city = await db.Cities.FirstOrDefaultAsync(x => x.BuiltIn);

        if (city == null)
            city = new();

        var lang = dbSeedOptions?.DefaultCulture.TwoLetterISOLanguageName;
        city.Name = BuildLocalizedText(lang ?? "en", Constants.BuiltInCity);
        city.Region = region;
        city.BuiltIn = true;

        return city;
    }

    private async Task<Company> SeedCompanyAsync()
    {
        var company = await db.Companies.FirstOrDefaultAsync(x => x.BuiltIn);

        if (company == null)
            company = new();

        var lang = dbSeedOptions?.DefaultCulture.TwoLetterISOLanguageName;
        company.Name = BuildLocalizedText(lang ?? "en", Constants.BuiltInCompany);
        company.CompanyType = CompanyTypes.SupplierOrServiceProvider;
        company.BuiltIn = true;

        company.ShortCode = dbSeedOptions?.CompanyShortCode;
        company.IntegrationId = dbSeedOptions?.CompanyExternalId;
        company.CompanyType = dbSeedOptions?.CompanyType ?? CompanyTypes.NotSpecified;

        return company;
    }

    private async Task<CompanyBranch> SeedCompanyBranchAsync(City city, Company company)
    {
        var companyBranch = await db.CompanyBranches.FirstOrDefaultAsync(x => x.BuiltIn);

        if (companyBranch == null)
            companyBranch = new();

        var lang = dbSeedOptions?.DefaultCulture.TwoLetterISOLanguageName;
        companyBranch.Name = BuildLocalizedText(lang ?? "en", Constants.BuiltInBranch);
        companyBranch.City = city;
        companyBranch.Region = city.Region;
        companyBranch.Company = company;
        companyBranch.BuiltIn = true;

        companyBranch.ShortCode = dbSeedOptions?.CompanyBranchShortCode;
        companyBranch.IntegrationId = dbSeedOptions?.CompanyBranchExternalId;

        return companyBranch;
    }

    private async Task SeedUserAsync(Country country, Region region, Company company, CompanyBranch companyBranch)
    {
        var user = await db.Users.FirstOrDefaultAsync(x => x.Username == Core.Constants.BuiltInUsername);

        var jsonTree = FullAccessTree.BuildJson(actionTrees);

        if (user == null)
        {
            user = new User();

            db.Users.Add(user);
        }

        user.FullName = "Super User";
        user.IsActive = true;
        user.Username = this.adminUserName;
        user.BuiltIn = true;
        user.RequireChangePassword = false;

        user.Country = country;
        user.Region = region;
        user.Company = company;
        user.CompanyBranch = companyBranch;

        user.AccessTree = jsonTree;

        var hash = HashService.GenerateHash(adminPassword);

        user.PasswordHash = hash.PasswordHash;

        user.Salt = hash.Salt;
    }

    private static string BuildLocalizedText(string language, string value)
        => JsonSerializer.Serialize(new Dictionary<string, string> { [language] = value });
}
