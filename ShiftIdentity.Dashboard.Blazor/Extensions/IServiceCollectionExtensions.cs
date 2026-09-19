
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShiftSoftware.ShiftIdentity.Blazor;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
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
        services.AddScoped<UserManagerService>();

        if (shiftIdentityDashboardBlazorOptions.StagedAuthority)
        {
            // The staged security flows of the account screens. The flow addresses the staged routes from the identity
            // API root and sets its own bearer and operation credentials, so it gets a raw client that never passes
            // through the ordinary bearer handler. A host that registered a flow of its own keeps it.
            services.TryAddScoped(sp => new StagedAuthorityHttpClient
            {
                BaseAddress = StagedAuthorityHttpClient.ApiRootOf(sp.GetRequiredService<ShiftIdentityBlazorOptions>().BaseUrl)
            });
            services.TryAddScoped(sp => new AuthenticationFlow(sp.GetRequiredService<StagedAuthorityHttpClient>(), sp.GetRequiredService<IdentitySession>()));
        }

        return services;
    }
}
