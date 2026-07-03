using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.TypeAuth.Core;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions;

public static class WebApplicationExtensions
{
    public static async Task<WebApplication> SeedDBAsync(this WebApplication app, string adminUserName, string adminPassword, DBSeedOptions? dBSeedOptions = null)
    {
        using var scope = app.Services.CreateScope();

        var scopedServices = scope.ServiceProvider;

        var db = scopedServices.GetRequiredService<ShiftIdentityDbContext>();

        var actionTrees = GetRegisteredActionTrees(scopedServices);

        await new DBSeed(db, actionTrees, adminUserName, adminPassword, dBSeedOptions).SeedAsync();

        return app;
    }

    public static async Task<WebApplication> SetFullAccessAsync(this WebApplication app, params string[] username)
    {
        using var scope = app.Services.CreateScope();
        var scopedServices = scope.ServiceProvider;

        var db = scopedServices.GetRequiredService<ShiftIdentityDbContext>();

        var users = await db.Users.Where(x => username.Contains(x.Username)).ToListAsync();

        var jsonTree = FullAccessTree.BuildJson(GetRegisteredActionTrees(scopedServices));

        foreach (var user in users)
        {
            user.AccessTree = jsonTree;
            user.BuiltIn = true;
        }

        await db.SaveChangesAsync();

        return app;
    }

    // The registered action trees (from TypeAuth), always including ShiftIdentity's own actions — the set
    // a built-in admin is granted full access over.
    private static List<Type> GetRegisteredActionTrees(IServiceProvider services)
    {
        var actionTrees = new List<Type>();

        var typeAuthService = services.GetService<ITypeAuthService>();

        if (typeAuthService is not null)
            actionTrees.AddRange(typeAuthService.GetRegisteredActionTrees());

        if (!actionTrees.Contains(typeof(ShiftIdentityActions)))
            actionTrees.Add(typeof(ShiftIdentityActions));

        return actionTrees;
    }
}
