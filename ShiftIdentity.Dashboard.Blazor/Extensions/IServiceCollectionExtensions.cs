using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;

public static class IServiceCollectionExtensions
{
    [Obsolete("This method is deprecated. Please add IOptions<ShiftIdentityDashboardBlazorOptions> instead by using AddOptions on IServiceCollection instead.")]
    public static IServiceCollection AddShiftIdentityDashboardBlazor(this IServiceCollection services, Action<ShiftIdentityDashboardBlazorOptions> shiftIdentityDashboardBlazorOptionsBuilder)
    {
        services.AddOptions<ShiftIdentityDashboardBlazorOptions>().Configure(shiftIdentityDashboardBlazorOptionsBuilder);
        return services;
    }

    [Obsolete("This method is deprecated. Please add IOptions<ShiftIdentityDashboardBlazorOptions> instead by using AddOptions on IServiceCollection instead.")]
    public static IServiceCollection AddShiftIdentityDashboardBlazor(this IServiceCollection services, ShiftIdentityDashboardBlazorOptions options)
    {
        return services.AddSingleton(Options.Create(options));
    }
}
