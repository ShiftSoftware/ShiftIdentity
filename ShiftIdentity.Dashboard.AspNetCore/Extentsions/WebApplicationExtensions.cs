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

        var actionTrees = new List<Type>();

        var typeAuthService = scopedServices.GetService<ITypeAuthService>();

        if (typeAuthService is not null)
        {
            foreach (var actionTree in typeAuthService.GetRegisteredActionTrees())
            {
                actionTrees.Add(actionTree);
            }
        }

        if (!actionTrees.Contains(typeof(ShiftIdentityActions)))
            actionTrees.Add(typeof(ShiftIdentityActions));

        await new DBSeed(db, actionTrees, adminUserName, adminPassword, dBSeedOptions).SeedAsync();

        return app;
    }

    public static async Task<WebApplication> SetFullAccessAsync(this WebApplication app, params string[] username)
    {
        using var scope = app.Services.CreateScope();
        var scopedServices = scope.ServiceProvider;

        var db = scopedServices.GetRequiredService<ShiftIdentityDbContext>();

        //Get list of users
        var users = await db.Users.Where(x => username.Contains(x.Username)).ToListAsync();

        //Get the full access trees
        var actionTrees = new List<Type>();

        var typeAuthService = scopedServices.GetService<ITypeAuthService>();

        if (typeAuthService is not null)
        {
            foreach (var actionTree in typeAuthService.GetRegisteredActionTrees())
            {
                actionTrees.Add(actionTree);
            }
        }

        if (!actionTrees.Contains(typeof(ShiftIdentityActions)))
            actionTrees.Add(typeof(ShiftIdentityActions));

        var tree = new Dictionary<string, object>();

        foreach (var item in actionTrees)
        {
            tree[item.Name] = new List<Access> { Access.Read, Access.Write, Access.Delete, Access.Maximum };
        }

        var jsonTree = System.Text.Json.JsonSerializer.Serialize(tree);

        foreach (var user in users)
            user.AccessTree = jsonTree;

        await db.SaveChangesAsync();

        return app;
    }
}
