using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Model.Enums;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.Data;

public class DBSeed
{
    private ShiftIdentityDbContext db;
    private DBSeedOptions? dbSeedOptions;

    private List<Type> actionTrees;
    private readonly string adminUserName;
    private readonly string adminPassword;

    /// <summary>
    /// Set by a host that enabled the identity authority: the built-in user then gets its security row with the
    /// user, the way every admitted creation does, so it can sign in before the host restarts.
    /// </summary>
    public bool CreateSecurityState { get; init; }

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

        var user = await SeedUserAsync(country, region, company, companyBranch);

        await db.SaveChangesAsync();

        if (CreateSecurityState && !await db.Set<Authentication.UserSecurityState>().AnyAsync(x => x.UserID == user.ID))
        {
            db.Add(Authentication.UserSecurityExpansion.CreateFor(user));
            await db.SaveChangesAsync();
        }
    }

    private async Task<Country> SeedCountryAsync()
    {
        var country = await db.Countries.FirstOrDefaultAsync(x => x.Name == Core.Constants.BuiltInCountry);
        if (country == null) {
            country = new();
        }

        country.Name = Core.Constants.BuiltInCountry;
        country.ShortCode = dbSeedOptions?.CountryShortCode;
        country.IntegrationId = dbSeedOptions?.CountryExternalId;
        country.CallingCode = dbSeedOptions?.CountryCallingCode ?? Core.Constants.BuiltInCountryCallingCode;
        country.IsProtected = true;

        return country;
    }

    private async Task<Region> SeedRegionAsync(Country country)
    {
        var region = await db.Regions.FirstOrDefaultAsync(x => x.Name == Core.Constants.BuiltInRegion);

        if (region == null)
            region = new();

        region.Name = Core.Constants.BuiltInRegion;
        region.ShortCode = dbSeedOptions?.RegionShortCode;
        region.IntegrationId = dbSeedOptions?.RegionExternalId;
        region.IsProtected = true;
        region.Country = country;

        return region;
    }

    private async Task<City> SeedCityAsync(Region region)
    {
        var city = await db.Cities.FirstOrDefaultAsync(x => x.Name == Core.Constants.BuiltInCity);

        if (city == null)
            city = new();

        city.Name = Core.Constants.BuiltInCity;
        city.Region = region;
        city.IsProtected = true;

        return city;
    }

    private async Task<Company> SeedCompanyAsync()
    {
        var company = await db.Companies.FirstOrDefaultAsync(x => x.Name == Core.Constants.BuiltInCompany);

        if (company == null)
            company = new();

        company.Name = Core.Constants.BuiltInCompany;
        company.CompanyType = CompanyTypes.SupplierOrServiceProvider;
        company.IsProtected = true;

        company.ShortCode = dbSeedOptions?.CompanyShortCode;
        company.IntegrationId = dbSeedOptions?.CompanyExternalId;
        company.CompanyType = dbSeedOptions?.CompanyType ?? CompanyTypes.NotSpecified;

        return company;
    }

    private async Task<CompanyBranch> SeedCompanyBranchAsync(City city, Company company)
    {
        var companyBranch = await db.CompanyBranches.FirstOrDefaultAsync(x => x.Name == Core.Constants.BuiltInBranch);

        if (companyBranch == null)
            companyBranch = new();

        companyBranch.Name = Core.Constants.BuiltInBranch;
        companyBranch.City = city;
        companyBranch.Region = city.Region;
        companyBranch.Company = company;
        companyBranch.IsProtected = true;

        companyBranch.ShortCode = dbSeedOptions?.CompanyBranchShortCode;
        companyBranch.IntegrationId = dbSeedOptions?.CompanyBranchExternalId;

        return companyBranch;
    }

    private async Task<User> SeedUserAsync(Country country, Region region, Company company, CompanyBranch companyBranch)
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
        user.IsProtected = true;
        user.RequireChangePassword = false;

        user.Country = country;
        user.Region = region;
        user.Company = company;
        user.CompanyBranch = companyBranch;

        user.AccessTree = jsonTree;

        var hash = HashService.GenerateHash(adminPassword);

        user.PasswordHash = hash.PasswordHash;

        user.Salt = hash.Salt;

        return user;
    }
}
