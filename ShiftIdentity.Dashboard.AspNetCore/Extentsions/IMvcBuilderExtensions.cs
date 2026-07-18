using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services.Interfaces;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Data.IRepositories;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Repositories;
using ShiftSoftware.ShiftIdentity.Data.Services;
using System.Reflection;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions;

public static class IMvcBuilderExtensions
{
    private static IMvcBuilder RegisterIShiftEntityFind(this IMvcBuilder builder)
    {
        Assembly repositoryAssembly = typeof(Marker).Assembly; // Adjust this as needed

        // Find all types in the assembly that implement IRepository<>
        var repositoryTypes = repositoryAssembly.GetTypes()
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IShiftEntityFind<>)) &&
                !t.IsInterface);

        // Register each IRepository<> implementation with its corresponding interface
        foreach (var repositoryType in repositoryTypes)
        {
            var interfaceType = repositoryType.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IShiftEntityFind<>));
            if (interfaceType != null)
            {
                builder.Services.AddScoped(interfaceType, repositoryType);
            }
        }

        return builder;
    }

    private static IMvcBuilder RegisterIShiftEntityPrepareForReplication(this IMvcBuilder builder)
    {
        Assembly repositoryAssembly=typeof(Marker).Assembly;

        var repositoryTypes = repositoryAssembly.GetTypes()
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IShiftEntityPrepareForReplicationAsync<>)) && 
                !t.IsInterface);

        // Register each IRepository<> implementation with its corresponding interface
        foreach (var repositoryType in repositoryTypes)
        {
            var interfaceType = repositoryType.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IShiftEntityPrepareForReplicationAsync<>));
            if (interfaceType != null)
            {
                builder.Services.AddScoped(interfaceType, repositoryType);
            }
        }

        return builder;
    }

    public static IMvcBuilder AddShiftIdentityDashboard<TDbContext>(this IMvcBuilder builder, ShiftIdentityConfiguration shiftIdentityConfiguration) where TDbContext : ShiftIdentityDbContext
    {
        builder.RegisterIShiftEntityFind();
        builder.RegisterIShiftEntityPrepareForReplication();

        builder.Services.TryAddSingleton(shiftIdentityConfiguration);
        builder.Services.TryAddSingleton(shiftIdentityConfiguration.ShiftIdentityFeatureLocking);
        builder.Services.TryAddSingleton(shiftIdentityConfiguration.DefaultDataLevelAccessOptions);
        // Also register the default data-level options under the framework base type so the built-in / attribute-driven
        // ShiftRepository path (which has no per-entity constructor to assign it) picks it up automatically.
        builder.Services.TryAddSingleton<DefaultDataLevelAccessOptions>(shiftIdentityConfiguration.DefaultDataLevelAccessOptions);

        // Generalized feature locking: one save-time validator replaces the 13 per-repository SaveChangesAsync
        // overrides. The built-in ShiftRepository invokes every registered IShiftEntitySaveValidator before saving.
        builder.Services.AddScoped<IShiftEntitySaveValidator, FeatureLockSaveValidator>();

        // Attribute-driven CRUD endpoints: scans ShiftIdentity.Data (where the entities live, alongside the
        // DbContext + EF Core) for entities decorated with [ShiftEntitySecureEndpoint<…>] (e.g. Brand) and
        // registers their built-in repository, TypeAuth action, DTO-map entry and source-generated mapper.
        // This is the DI half; the host maps the routes with app.MapShiftEntityEndpoints<DB>() after calling
        // AddShiftEntityWeb(x => x.AddShiftIdentityDataAssembly()).
        builder.Services.RegisterShiftRepositories(typeof(Marker).Assembly);

        builder.Services.AddShiftIdentityAuthCoreServices();

        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IAppRepository, AppRepository>();
        builder.Services.AddScoped<AccessTreeRepository>();

        builder.Services.AddScoped<UserRepository>();

        builder.Services.AddScoped<IClaimService, ClaimService>();

        // Brand / Service / Department / Country / Region / City / App CRUD is attribute-driven — those entities
        // carry [ShiftEntitySecureEndpoint<…>] and use the built-in repository + source-generated mapper, so they
        // need no repository registration here (RegisterShiftRepositories wires them). App keeps a slim
        // AppRepository, registered above only as IAppRepository for the OAuth AuthCodeService.
        builder.Services.AddScoped<CompanyRepository>();
        builder.Services.AddScoped<CompanyBranchRepository>();
        builder.Services.AddScoped<TeamRepository>();
        builder.Services.AddScoped<CompanyCalendarRepository>();
        builder.Services.AddScoped<CalendarService>();

        builder.Services.AddScoped<ShiftIdentityDbContext>(x=> x.GetRequiredService<TDbContext>());

        // Step-up scheme + policies (shared with the fake host; defined in ShiftIdentity.AspNetCore).
        builder.AddStepUpAuthorization();

        return builder;
    }
}
