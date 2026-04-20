using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.AspNetCore;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services.Interfaces;
using ShiftSoftware.ShiftIdentity.Core.IRepositories;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extensions;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Repositories;
using ShiftSoftware.ShiftIdentity.Data.Services;
using System.Reflection;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions;

public static class IMvcBuilderExtensions
{
    [Obsolete("This method is deprecated. Please use AddShiftIdentityDashboardApi<TDbContext> on IServiceCollection instead.")]
    public static IMvcBuilder AddShiftIdentityDashboard<TDbContext>(this IMvcBuilder builder, ShiftIdentityConfiguration shiftIdentityConfiguration) where TDbContext : ShiftIdentityDbContext
    {
        builder.Services.AddShiftIdentityDashboardApi<TDbContext>(shiftIdentityConfiguration);
        return builder;
    }
}
