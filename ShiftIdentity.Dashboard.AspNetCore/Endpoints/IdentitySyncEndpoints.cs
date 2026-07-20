using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.TypeAuth.AspNetCore.EndpointFilters;
using ShiftSoftware.TypeAuth.Core;
using System.Linq;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Endpoints;

// The dev/staging "pull live DB into local" sync, ported verbatim from IdentitySyncController (route + verb
// byte-identical: GET api/IdentitySync/pull-live-db-data). The [TypeAuth<ShiftIdentityActions>(PullLiveData,
// Maximum)] attribute becomes .RequireTypeAuth(typeof(ShiftIdentityActions), PullLiveData, Access.Maximum). The
// 16 Copy* helpers are unchanged (identity-insert inside a transaction, per table). Composed by
// MapShiftIdentityDashboard().
internal static class IdentitySyncEndpoints
{
    public static IEndpointRouteBuilder MapIdentitySyncEndpoints(this IEndpointRouteBuilder app)
    {
        // GET api/IdentitySync/pull-live-db-data — Dev/Staging only; wipes local + copies the live identity DB.
        // [FromServices] is REQUIRED on the DbContext params: LiveShiftIdentityDbContext isn't registered in every
        // host (e.g. the Test host), and without the attribute minimal APIs infer an unregistered complex param as a
        // request body — illegal on a GET — which fails at MAP time (and cascades to a host-startup error). The MVC
        // controller never hit this because it resolved the context via constructor DI at request time.
        app.MapGet("api/IdentitySync/pull-live-db-data",
            async ([FromServices] ShiftIdentityDbContext db, [FromServices] LiveShiftIdentityDbContext liveDb, [FromServices] IHostEnvironment hostEnvironment) =>
            {
                if (!(hostEnvironment.IsDevelopment() || hostEnvironment.IsStaging()))
                    return Results.Json("Only available in Staging & Development", statusCode: StatusCodes.Status401Unauthorized);

                await db.UserLogs.ExecuteDeleteAsync();
                await db.UserAccessTrees.ExecuteDeleteAsync();
                await db.TeamUsers.ExecuteDeleteAsync();
                await db.Users.Where(x => !x.IsProtected).ExecuteDeleteAsync();
                await db.AccessTrees.ExecuteDeleteAsync();
                await db.CompanyBranchBrands.ExecuteDeleteAsync();
                await db.CompanyBranchDepartments.ExecuteDeleteAsync();
                await db.CompanyBranchServices.ExecuteDeleteAsync();
                await db.CompanyBranches.Where(x => !x.IsProtected).ExecuteDeleteAsync();
                await db.Cities.Where(x => !x.IsProtected).ExecuteDeleteAsync();
                await db.Regions.Where(x => !x.IsProtected).ExecuteDeleteAsync();
                await db.Countries.Where(x => !x.IsProtected).ExecuteDeleteAsync();
                await db.Departments.ExecuteDeleteAsync();
                await db.Services.ExecuteDeleteAsync();
                await db.Brands.ExecuteDeleteAsync();
                await db.Teams.ExecuteDeleteAsync();
                await db.Companies.Where(x => !x.IsProtected).ExecuteDeleteAsync();

                await CopyCountries(db, liveDb);
                await CopyRegions(db, liveDb);
                await CopyCities(db, liveDb);
                await CopyBrands(db, liveDb);
                await CopyDepartments(db, liveDb);
                await CopyServices(db, liveDb);
                await CopyCompanies(db, liveDb);
                await CopyCompanyBranches(db, liveDb);
                await CopyAccessTrees(db, liveDb);
                await CopyUsers(db, liveDb);
                await CopyTeams(db, liveDb);
                await CopyUserAccessTrees(db, liveDb);
                await CopyTeamUsers(db, liveDb);
                await CopyCompanyBranchBrands(db, liveDb);
                await CopyCompanyBranchDepartments(db, liveDb);
                await CopyCompanyBranchServices(db, liveDb);

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

                return Results.Ok();
            })
            .RequireTypeAuth(typeof(ShiftIdentityActions), nameof(ShiftIdentityActions.PullLiveData), Access.Maximum);

        return app;
    }

    private static async Task CopyCountries(ShiftIdentityDbContext db, LiveShiftIdentityDbContext liveDb)
    {
        var liveData = await liveDb.Countries.Where(x => !x.IsProtected).AsNoTracking().ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Countries ON");
            db.Countries.AddRange(liveData);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Countries OFF");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CopyRegions(ShiftIdentityDbContext db, LiveShiftIdentityDbContext liveDb)
    {
        var liveData = await liveDb.Regions.Where(x => !x.IsProtected).AsNoTracking().ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Regions ON");
            db.Regions.AddRange(liveData);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Regions OFF");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CopyCities(ShiftIdentityDbContext db, LiveShiftIdentityDbContext liveDb)
    {
        var liveData = await liveDb.Cities.Where(x => !x.IsProtected).AsNoTracking().ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Cities ON");
            db.Cities.AddRange(liveData);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Cities OFF");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CopyBrands(ShiftIdentityDbContext db, LiveShiftIdentityDbContext liveDb)
    {
        var liveData = await liveDb.Brands.AsNoTracking().ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Brands ON");
            db.Brands.AddRange(liveData);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Brands OFF");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CopyDepartments(ShiftIdentityDbContext db, LiveShiftIdentityDbContext liveDb)
    {
        var liveData = await liveDb.Departments.AsNoTracking().ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Departments ON");
            db.Departments.AddRange(liveData);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Departments OFF");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CopyServices(ShiftIdentityDbContext db, LiveShiftIdentityDbContext liveDb)
    {
        var liveData = await liveDb.Services.AsNoTracking().ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Services ON");
            db.Services.AddRange(liveData);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Services OFF");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CopyCompanies(ShiftIdentityDbContext db, LiveShiftIdentityDbContext liveDb)
    {
        var liveData = await liveDb.Companies.Where(x => !x.IsProtected).AsNoTracking().ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Companies ON");
            db.Companies.AddRange(liveData);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Companies OFF");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CopyCompanyBranches(ShiftIdentityDbContext db, LiveShiftIdentityDbContext liveDb)
    {
        var liveData = await liveDb.CompanyBranches.Where(x => !x.IsProtected).AsNoTracking().ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.CompanyBranches ON");
            db.CompanyBranches.AddRange(liveData);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.CompanyBranches OFF");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CopyAccessTrees(ShiftIdentityDbContext db, LiveShiftIdentityDbContext liveDb)
    {
        var liveData = await liveDb.AccessTrees.AsNoTracking().ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.AccessTrees ON");
            db.AccessTrees.AddRange(liveData);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.AccessTrees OFF");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CopyUsers(ShiftIdentityDbContext db, LiveShiftIdentityDbContext liveDb)
    {
        var liveData = await liveDb.Users.Where(x => !x.IsProtected).AsNoTracking().ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Users ON");
            db.Users.AddRange(liveData);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Users OFF");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CopyTeams(ShiftIdentityDbContext db, LiveShiftIdentityDbContext liveDb)
    {
        var liveData = await liveDb.Teams.AsNoTracking().ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Teams ON");
            db.Teams.AddRange(liveData);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.Teams OFF");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CopyUserAccessTrees(ShiftIdentityDbContext db, LiveShiftIdentityDbContext liveDb)
    {
        var liveData = await liveDb.UserAccessTrees.AsNoTracking().ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.UserAccessTrees ON");
            db.UserAccessTrees.AddRange(liveData);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.UserAccessTrees OFF");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CopyTeamUsers(ShiftIdentityDbContext db, LiveShiftIdentityDbContext liveDb)
    {
        var liveData = await liveDb.TeamUsers.AsNoTracking().ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.TeamUsers ON");
            db.TeamUsers.AddRange(liveData);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.TeamUsers OFF");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CopyCompanyBranchBrands(ShiftIdentityDbContext db, LiveShiftIdentityDbContext liveDb)
    {
        var liveData = await liveDb.CompanyBranchBrands.AsNoTracking().ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.CompanyBranchBrands ON");
            db.CompanyBranchBrands.AddRange(liveData);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.CompanyBranchBrands OFF");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CopyCompanyBranchDepartments(ShiftIdentityDbContext db, LiveShiftIdentityDbContext liveDb)
    {
        var liveData = await liveDb.CompanyBranchDepartments.AsNoTracking().ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.CompanyBranchDepartments ON");
            db.CompanyBranchDepartments.AddRange(liveData);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.CompanyBranchDepartments OFF");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CopyCompanyBranchServices(ShiftIdentityDbContext db, LiveShiftIdentityDbContext liveDb)
    {
        var liveData = await liveDb.CompanyBranchServices.AsNoTracking().ToListAsync();

        using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT ShiftIdentity.CompanyBranchServices ON");
            db.CompanyBranchServices.AddRange(liveData);
            await db.SaveChangesAsync();
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
