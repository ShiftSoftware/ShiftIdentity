using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services.Interfaces;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data;
using ShiftSoftware.ShiftIdentity.AspNetCore.IRepositories;
using ShiftSoftware.ShiftIdentity.AspNetCore;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions;

public static class IMvcBuilderExtensions
{
    public static IMvcBuilder AddShiftIdentityDashboard<TDbContext>(this IMvcBuilder builder, ShiftIdentityConfiguration shiftIdentityConfiguration) where TDbContext : ShiftIdentityDB
    {
        builder.Services.TryAddSingleton(shiftIdentityConfiguration);

        builder.Services.AddSingleton<AuthCodeStoreService>();
        builder.Services.AddScoped<AuthCodeService>();
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<TokenService>();
        builder.Services.AddScoped<HashService>();

        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IAppRepository, AppRepository>();
        builder.Services.AddScoped<AccessTreeRepository>();

        builder.Services.AddScoped<UserRepository>();
        builder.Services.AddScoped<AppRepository>();
        
        builder.Services.AddScoped<IClaimService, ClaimService>();

        builder.Services.AddScoped<ShiftIdentityDB, TDbContext>();

        return builder;
    }
}
