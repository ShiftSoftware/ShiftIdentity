using Microsoft.AspNetCore.Builder;
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
}
