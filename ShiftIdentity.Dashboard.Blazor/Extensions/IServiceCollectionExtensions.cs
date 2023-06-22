
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;

public static class IServiceCollectionExtensions
{
    public static IServiceCollection AddShiftIdentityDashboardBlazor(this IServiceCollection services, Action<ShiftIdentityDashboardBlazorOptions> shiftIdentityDashboardBlazorOptionsBuilder)
    {
        var o = new ShiftIdentityDashboardBlazorOptions();

        shiftIdentityDashboardBlazorOptionsBuilder.Invoke(o);

        services.AddShiftIdentityDashboardBlazor(o);

        return services;
    }
    public static IServiceCollection AddShiftIdentityDashboardBlazor(this IServiceCollection services, ShiftIdentityDashboardBlazorOptions shiftIdentityDashboardBlazorOptions)
    {
        services.AddSingleton(shiftIdentityDashboardBlazorOptions);

        services.AddScoped<AuthService>();
        services.AddScoped<HttpService>();
        services.AddScoped<StorageService>();
        services.AddScoped<UserManagerService>();

        return services;
    }
}
