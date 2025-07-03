using EntityFrameworkCore.Triggered.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.TypeAuth.AspNetCore;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers;


[Route("api/[controller]")]
public class IdentitySyncController : ControllerBase
{
    private readonly ShiftIdentityDbContext db;
    private readonly LiveShiftIdentityDbContext liveDb;
    private readonly IHostEnvironment hostEnvironment;


    public IdentitySyncController(ShiftIdentityDbContext db, LiveShiftIdentityDbContext liveDb, IHostEnvironment hostEnvironment)
    {
        this.db = db;
        this.liveDb = liveDb;
        this.hostEnvironment = hostEnvironment;
    }

    [TypeAuth<ShiftIdentityActions>(nameof(ShiftIdentityActions.PullLiveData), TypeAuth.Core.Access.Maximum)]
    [HttpGet("pull-live-db-data")]
    public async Task<IActionResult> PullLiveData()
    {
        if (!(this.hostEnvironment.IsDevelopment() || this.hostEnvironment.IsStaging()))
            return Unauthorized("Only available in Staging & Development");

        await db.UserLogs.ExecuteDeleteAsync();
        await db.UserAccessTrees.ExecuteDeleteAsync();
        await db.TeamUsers.ExecuteDeleteAsync();
        await db.Users.Where(x => !x.BuiltIn).ExecuteDeleteAsync();
        await db.AccessTrees.ExecuteDeleteAsync();
        await db.CompanyBranchBrands.ExecuteDeleteAsync();
        await db.CompanyBranchDepartments.ExecuteDeleteAsync();
        await db.CompanyBranchServices.ExecuteDeleteAsync();
        await db.CompanyBranches.Where(x => !x.BuiltIn).ExecuteDeleteAsync();
        await db.Cities.Where(x => !x.BuiltIn).ExecuteDeleteAsync();
        await db.Regions.Where(x => !x.BuiltIn).ExecuteDeleteAsync();
        await db.Countries.Where(x => !x.BuiltIn).ExecuteDeleteAsync();
        await db.Departments.ExecuteDeleteAsync();
        await db.Services.ExecuteDeleteAsync();
        await db.Brands.ExecuteDeleteAsync();
        await db.Teams.ExecuteDeleteAsync();
        await db.Companies.Where(x => !x.BuiltIn).ExecuteDeleteAsync();

        await this.CopyCountries();
        await this.CopyRegions();
        await this.CopyCities();
        await this.CopyBrands();
        await this.CopyDepartments();
        await this.CopyServices();
        await this.CopyCompanies();
        await this.CopyCompanyBranches();
        await this.CopyAccessTrees();
        await this.CopyUsers();
        await this.CopyTeams();
        await this.CopyUserAccessTrees();
        await this.CopyTeamUsers();
        await this.CopyCompanyBranchBrands();
        await this.CopyCompanyBranchDepartments();
        await this.CopyCompanyBranchServices();

        #region Trigger Replication

        foreach (var item in await db.Countries.ToListAsync())
            db.Countries.Entry(item).State = EntityState.Modified;

        foreach (var item in await db.Regions.ToListAsync())
            db.Regions.Entry(item).State = EntityState.Modified;

        foreach (var item in await db.Cities.ToListAsync())
            db.Cities.Entry(item).State = EntityState.Modified;

        foreach (var item in await db.Brands.ToListAsync())
            db.Brands.Entry(item).State = EntityState.Modified;

        foreach (var item in await db.Departments.ToListAsync())
            db.Departments.Entry(item).State = EntityState.Modified;

        foreach (var item in await db.Services.ToListAsync())
            db.Services.Entry(item).State = EntityState.Modified;

        foreach (var item in await db.Companies.ToListAsync())
            db.Companies.Entry(item).State = EntityState.Modified;

        foreach (var item in await db.CompanyBranches.ToListAsync())
            db.CompanyBranches.Entry(item).State = EntityState.Modified;

        foreach (var item in await db.AccessTrees.ToListAsync())
            db.AccessTrees.Entry(item).State = EntityState.Modified;

        foreach (var item in await db.Users.ToListAsync())
            db.Users.Entry(item).State = EntityState.Modified;

        foreach (var item in await db.Teams.ToListAsync())
            db.Teams.Entry(item).State = EntityState.Modified;

        foreach (var item in await db.UserAccessTrees.ToListAsync())
            db.UserAccessTrees.Entry(item).State = EntityState.Modified;

        foreach (var item in await db.TeamUsers.ToListAsync())
            db.TeamUsers.Entry(item).State = EntityState.Modified;

        foreach (var item in await db.CompanyBranchBrands.ToListAsync())
            db.CompanyBranchBrands.Entry(item).State = EntityState.Modified;

        foreach (var item in await db.CompanyBranchDepartments.ToListAsync())
            db.CompanyBranchDepartments.Entry(item).State = EntityState.Modified;

        foreach (var item in await db.CompanyBranchServices.ToListAsync())
            db.CompanyBranchServices.Entry(item).State = EntityState.Modified;

        await db.SaveChangesAsync();

        #endregion

        return Ok();
    }

    private async Task CopyCountries()
    {
        var liveData = await liveDb
            .Countries
            .Where(x => !x.BuiltIn)
            .AsNoTracking()
            .ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // Enable identity insert — inside the same transaction
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Countries ON");

            db.Countries.AddRange(liveData);
            await db.SaveChangesWithoutTriggersAsync();

            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Countries OFF");

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task CopyRegions()
    {
        var liveData = await liveDb
            .Regions
            .Where(x => !x.BuiltIn)
            .AsNoTracking()
            .ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // Enable identity insert — inside the same transaction
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Regions ON");

            db.Regions.AddRange(liveData);
            await db.SaveChangesWithoutTriggersAsync();

            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Regions OFF");

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task CopyCities()
    {
        var liveData = await liveDb
            .Cities
            .Where(x => !x.BuiltIn)
            .AsNoTracking()
            .ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // Enable identity insert — inside the same transaction
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Cities ON");

            db.Cities.AddRange(liveData);
            await db.SaveChangesWithoutTriggersAsync();

            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Cities OFF");

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task CopyBrands()
    {
        var liveData = await liveDb
            .Brands
            .AsNoTracking()
            .ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // Enable identity insert — inside the same transaction
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Brands ON");

            db.Brands.AddRange(liveData);
            await db.SaveChangesWithoutTriggersAsync();

            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Brands OFF");

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task CopyDepartments()
    {
        var liveData = await liveDb
            .Departments
            .AsNoTracking()
            .ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // Enable identity insert — inside the same transaction
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Departments ON");

            db.Departments.AddRange(liveData);
            await db.SaveChangesWithoutTriggersAsync();

            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Departments OFF");

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task CopyServices()
    {
        var liveData = await liveDb
            .Services
            .AsNoTracking()
            .ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // Enable identity insert — inside the same transaction
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Services ON");

            db.Services.AddRange(liveData);
            await db.SaveChangesWithoutTriggersAsync();

            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Services OFF");

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task CopyCompanies()
    {
        var liveData = await liveDb
            .Companies
            .Where(x => !x.BuiltIn)
            .AsNoTracking()
            .ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // Enable identity insert — inside the same transaction
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Companies ON");

            db.Companies.AddRange(liveData);
            await db.SaveChangesWithoutTriggersAsync();

            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Companies OFF");

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task CopyCompanyBranches()
    {
        var liveData = await liveDb
            .CompanyBranches
            .Where(x => !x.BuiltIn)
            .AsNoTracking()
            .ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // Enable identity insert — inside the same transaction
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.CompanyBranches ON");

            db.CompanyBranches.AddRange(liveData);
            await db.SaveChangesWithoutTriggersAsync();

            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.CompanyBranches OFF");

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task CopyAccessTrees()
    {
        var liveData = await liveDb
            .AccessTrees
            .AsNoTracking()
            .ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // Enable identity insert — inside the same transaction
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.AccessTrees ON");

            db.AccessTrees.AddRange(liveData);
            await db.SaveChangesWithoutTriggersAsync();

            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.AccessTrees OFF");

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task CopyUsers()
    {
        var liveData = await liveDb
            .Users
            .Where(x => !x.BuiltIn)
            .AsNoTracking()
            .ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // Enable identity insert — inside the same transaction
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Users ON");

            db.Users.AddRange(liveData);
            await db.SaveChangesWithoutTriggersAsync();

            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Users OFF");

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task CopyTeams()
    {
        var liveData = await liveDb
            .Teams
            .AsNoTracking()
            .ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // Enable identity insert — inside the same transaction
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Teams ON");

            db.Teams.AddRange(liveData);
            await db.SaveChangesWithoutTriggersAsync();

            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Teams OFF");

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task CopyUserAccessTrees()
    {
        var liveData = await liveDb
            .UserAccessTrees
            .AsNoTracking()
            .ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // Enable identity insert — inside the same transaction
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.UserAccessTrees ON");

            db.UserAccessTrees.AddRange(liveData);
            await db.SaveChangesWithoutTriggersAsync();

            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.UserAccessTrees OFF");

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task CopyTeamUsers()
    {
        var liveData = await liveDb
            .TeamUsers
            .AsNoTracking()
            .ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // Enable identity insert — inside the same transaction
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.TeamUsers ON");

            db.TeamUsers.AddRange(liveData);
            await db.SaveChangesWithoutTriggersAsync();

            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.TeamUsers OFF");

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task CopyCompanyBranchBrands()
    {
        var liveData = await liveDb
            .CompanyBranchBrands
            .AsNoTracking()
            .ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // Enable identity insert — inside the same transaction
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.CompanyBranchBrands ON");

            db.CompanyBranchBrands.AddRange(liveData);
            await db.SaveChangesWithoutTriggersAsync();

            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.CompanyBranchBrands OFF");

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task CopyCompanyBranchDepartments()
    {
        var liveData = await liveDb
            .CompanyBranchDepartments
            .AsNoTracking()
            .ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // Enable identity insert — inside the same transaction
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.CompanyBranchDepartments ON");

            db.CompanyBranchDepartments.AddRange(liveData);
            await db.SaveChangesWithoutTriggersAsync();

            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.CompanyBranchDepartments OFF");

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task CopyCompanyBranchServices()
    {
        var liveData = await liveDb
            .CompanyBranchServices
            .AsNoTracking()
            .ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // Enable identity insert — inside the same transaction
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.CompanyBranchServices ON");

            db.CompanyBranchServices.AddRange(liveData);
            await db.SaveChangesWithoutTriggersAsync();

            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.CompanyBranchServices OFF");

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}