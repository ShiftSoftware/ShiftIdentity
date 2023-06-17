using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.Core.DTOs.App;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core.Models;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;
using ShiftSoftware.ShiftIdentity.Core.Repositories;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc;

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
