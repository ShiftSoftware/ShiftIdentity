using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.AspNetCore;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services.Interfaces;
using ShiftSoftware.ShiftIdentity.Core.IRepositories;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Repositories;
using ShiftSoftware.ShiftIdentity.Data.Services;
using System.Reflection;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShiftIdentityDashboard<TDbContext>(this IServiceCollection services, ShiftIdentityConfiguration shiftIdentityConfiguration) where TDbContext : ShiftIdentityDbContext
    {
        services.RegisterIShiftEntityFind();
        services.RegisterIShiftEntityPrepareForReplication();

        services.TryAddSingleton(shiftIdentityConfiguration);
        services.TryAddSingleton(shiftIdentityConfiguration.ShiftIdentityFeatureLocking);
        services.TryAddSingleton(shiftIdentityConfiguration.DefaultDataLevelAccessOptions);

        services.AddSingleton<AuthCodeStoreService>();
        services.AddScoped<AuthCodeService>();
        services.AddScoped<AuthService>();
        services.AddScoped<TokenService>();
        services.AddScoped<Core.HashService>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAppRepository, AppRepository>();
        services.AddScoped<AccessTreeRepository>();

        services.AddScoped<UserRepository>();
        services.AddScoped<AppRepository>();

        services.AddScoped<IClaimService, ClaimService>();

        services.AddScoped<DepartmentRepository>();
        services.AddScoped<CityRepository>();
        services.AddScoped<RegionRepository>();
        services.AddScoped<ServiceRepository>();
        services.AddScoped<BrandRepository>();
        services.AddScoped<CompanyRepository>();
        services.AddScoped<CompanyBranchRepository>();
        services.AddScoped<TeamRepository>();
        services.AddScoped<CountryRepository>();
        services.AddScoped<CompanyCalendarRepository>();
        services.AddScoped<CalendarService>();

        services.AddScoped<ShiftIdentityDbContext>(x => x.GetRequiredService<TDbContext>());

        return services;
    }

    private static IServiceCollection RegisterIShiftEntityFind(this IServiceCollection services)
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
                services.AddScoped(interfaceType, repositoryType);
            }
        }

        return services;
    }

    private static IServiceCollection RegisterIShiftEntityPrepareForReplication(this IServiceCollection services)
    {
        Assembly repositoryAssembly = typeof(Marker).Assembly;

        var repositoryTypes = repositoryAssembly.GetTypes()
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IShiftEntityPrepareForReplicationAsync<>)) &&
                !t.IsInterface);

        // Register each IRepository<> implementation with its corresponding interface
        foreach (var repositoryType in repositoryTypes)
        {
            var interfaceType = repositoryType.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IShiftEntityPrepareForReplicationAsync<>));
            if (interfaceType != null)
            {
                services.AddScoped(interfaceType, repositoryType);
            }
        }

        return services;
    }
}
