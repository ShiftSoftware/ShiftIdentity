using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.AspNetCore;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services.Interfaces;
using ShiftSoftware.ShiftIdentity.Core.IRepositories;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Repositories;
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

        builder.Services.AddSingleton<AuthCodeStoreService>();
        builder.Services.AddScoped<AuthCodeService>();
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<TokenService>();
        builder.Services.AddScoped<Core.HashService>();

        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IAppRepository, AppRepository>();
        builder.Services.AddScoped<AccessTreeRepository>();

        builder.Services.AddScoped<UserRepository>();
        builder.Services.AddScoped<AppRepository>();
        
        builder.Services.AddScoped<IClaimService, ClaimService>();

        builder.Services.AddScoped<DepartmentRepository>();
        builder.Services.AddScoped<CityRepository>();
        builder.Services.AddScoped<RegionRepository>();
        builder.Services.AddScoped<ServiceRepository>();
        builder.Services.AddScoped<BrandRepository>();
        builder.Services.AddScoped<CompanyRepository>();
        builder.Services.AddScoped<CompanyBranchRepository>();
        builder.Services.AddScoped<TeamRepository>();

        builder.Services.AddScoped<ShiftIdentityDbContext>(x=> x.GetRequiredService<TDbContext>());

        return builder;
    }
}
