using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftIdentity.Data;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers;


[Route("api/[controller]")]
public class IdentitySyncController : ControllerBase
{
    private readonly ShiftIdentityDbContext db;
    private readonly LiveShiftIdentityDbContext liveDb;


    public IdentitySyncController(ShiftIdentityDbContext db, LiveShiftIdentityDbContext liveDb)
    {
        this.db = db;
        this.liveDb = liveDb;
    }

    [AllowAnonymous]
    [HttpGet("pull-live-db-data")]
    public async Task<IActionResult> PullLiveData()
    {
        await db.UserLogs.ExecuteDeleteAsync();
        await db.Users.Where(x => !x.BuiltIn).ExecuteDeleteAsync();
        await db.AccessTrees.ExecuteDeleteAsync();
        await db.UserAccessTrees.ExecuteDeleteAsync();
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

        return Ok();
    }

    private async Task CopyCountries()
    {
        var liveData = await liveDb
            .Countries
            .AsNoTracking()
            .ToListAsync();

        await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT Countries ON");

        db.Countries.AddRange(liveData);
        await db.SaveChangesAsync();

        // Disable identity insert
        await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT Countries OFF");
    }
}