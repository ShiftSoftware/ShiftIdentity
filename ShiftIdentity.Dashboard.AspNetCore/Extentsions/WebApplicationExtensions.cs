using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions;

public static class WebApplicationExtensions
{
    public static async Task<WebApplication> SeedDBAsync(this WebApplication app, List<Type>? actionTrees, string superUserPassword)
    {
        if (actionTrees is null)
            actionTrees = new List<Type>();

        actionTrees.Add(typeof(ShiftIdentityActions));

        using var scope = app.Services.CreateScope();

        var scopedServices = scope.ServiceProvider;

        var db = scopedServices.GetRequiredService<ShiftIdentityDB>();

        await new DBSeed(db, actionTrees, superUserPassword).SeedAsync();

        return app;
    }
}
