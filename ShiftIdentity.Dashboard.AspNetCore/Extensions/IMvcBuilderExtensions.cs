using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.AspNetCore;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extensions;
using ShiftSoftware.ShiftIdentity.Data;


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
