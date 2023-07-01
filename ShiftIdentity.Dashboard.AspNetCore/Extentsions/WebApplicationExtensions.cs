﻿using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data;
using ShiftSoftware.TypeAuth.AspNetCore.Services;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions;

public static class WebApplicationExtensions
{
    public static async Task<WebApplication> SeedDBAsync(this WebApplication app, string superUserPassword)
    {
        using var scope = app.Services.CreateScope();

        var scopedServices = scope.ServiceProvider;

        var db = scopedServices.GetRequiredService<ShiftIdentityDB>();

        var actionTrees = new List<Type>();

        var typeAuthService = scopedServices.GetService<TypeAuthService>();

        if (typeAuthService is not null)
        {
            foreach (var actionTree in typeAuthService.GetRegisteredActionTrees())
            {
                actionTrees.Add(actionTree);
            }
        }

        if (!actionTrees.Contains(typeof(ShiftIdentityActions)))
            actionTrees.Add(typeof(ShiftIdentityActions));

        await new DBSeed(db, actionTrees, superUserPassword).SeedAsync();

        return app;
    }
}
