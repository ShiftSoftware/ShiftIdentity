
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services;
using System.Text.Json;

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
    public static IServiceCollection AddShiftIdentityDashboardBlazor(this IServiceCollection services, ShiftIdentityDashboardBlazorOptions options)
    {
        if (options.ShiftIdentityHostingType == 0)
        {
            options.ShiftIdentityHostingType = string.IsNullOrWhiteSpace(options.ExternalIdentityApiUrl)
                ? ShiftIdentityHostingTypes.Internal
                : ShiftIdentityHostingTypes.External;
        }

        services.AddSingleton(options);

        services.AddScoped<AuthService>();
        services.AddScoped<HttpService>();
        services.AddScoped<UserManagerService>();

        return services;
    }
}
