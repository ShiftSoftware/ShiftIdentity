using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Entities;
//using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.TypeAuth.Core;

namespace ShiftSoftware.ShiftIdentity.Data;

public class DBSeed
{
    private ShiftIdentityDB db;
    private DBSeedOptions? dbSeedOptions;

    private List<Type> actionTrees;
    private readonly string superUserPassword;

    public DBSeed(ShiftIdentityDB db, List<Type> actionTrees, string superUserPassword, DBSeedOptions? dBSeedOptions)
    {
        this.db = db;
        this.actionTrees = actionTrees;
        this.superUserPassword = superUserPassword;
        dbSeedOptions = dBSeedOptions;
    }

    public async Task SeedAsync()
    {
        Region region = await SeedRegionAsync();

        Company company = await SeedCompanyAsync();

        CompanyBranch companyBranch = await SeedCompanyBranchAsync(region, company);

        await SeedUserAsync(region, company, companyBranch);

        await db.SaveChangesAsync();
    }

    private async Task<Region> SeedRegionAsync()
    {
        var region = await db.Regions.FirstOrDefaultAsync(x => x.Name == Core.Constants.BuiltInRegion);

        if (region == null)
            region = new();

        region.Name = Core.Constants.BuiltInRegion;
        region.ShortCode = dbSeedOptions?.RegionShortCode;
        region.ExternalId = dbSeedOptions?.RegionExternalId;
        region.BuiltIn = true;

        return region;
    }

    private async Task<Company> SeedCompanyAsync()
    {
        var company = await db.Companies.FirstOrDefaultAsync(x => x.Name == Core.Constants.BuiltInCompany);

        if (company == null)
            company = new();

        company.Name = Core.Constants.BuiltInCompany;
        company.CompanyType = Core.Enums.CompanyTypes.SupplierOrServiceProvider;
        company.BuiltIn = true;

        company.ShortCode = dbSeedOptions?.CompanyShortCode;
        company.ExternalId = dbSeedOptions?.CompanyExternalId;
        company.AlternativeExternalId = dbSeedOptions?.CompanyAlternativeExternalId;
        company.CompanyType = dbSeedOptions?.CompanyType ?? Core.Enums.CompanyTypes.NotSpecified;

        return company;
    }

    private async Task<CompanyBranch> SeedCompanyBranchAsync(Region region, Company company)
    {
        var companyBranch = await db.CompanyBranches.FirstOrDefaultAsync(x => x.Name == Core.Constants.BuiltInBranch);

        if (companyBranch == null)
            companyBranch = new();

        companyBranch.Name = Core.Constants.BuiltInBranch;
        companyBranch.Region = region;
        companyBranch.Company = company;
        companyBranch.BuiltIn = true;

        companyBranch.ShortCode = dbSeedOptions?.CompanyBranchShortCode;
        companyBranch.ExternalId = dbSeedOptions?.CompanyBranchExternalId;

        return companyBranch;
    }

    private async Task SeedUserAsync(Region region, Company company, CompanyBranch companyBranch)
    {
        var user = await db.Users.FirstOrDefaultAsync(x => x.Username == Core.Constants.BuiltInUsername);

        var tree = new Dictionary<string, object>();

        foreach (var item in actionTrees)
        {
            tree[item.Name] = new List<Access> { Access.Read, Access.Write, Access.Delete, Access.Maximum };
        }

        var jsonTree = System.Text.Json.JsonSerializer.Serialize(tree);

        if (user == null)
        {
            user = new User();

            db.Users.Add(user);
        }

        user.FullName = "Super User";
        user.IsActive = true;
        user.Username = "SuperUser";
        user.BuiltIn = true;
        user.RequireChangePassword = false;

        user.Region = region;
        user.Company = company;
        user.CompanyBranch = companyBranch;

        user.AccessTree = jsonTree;

        var hash = HashService.GenerateHash(superUserPassword);

        user.PasswordHash = hash.PasswordHash;

        user.Salt = hash.Salt;
    }
}
