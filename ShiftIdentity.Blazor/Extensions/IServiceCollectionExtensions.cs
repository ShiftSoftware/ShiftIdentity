using Blazored.LocalStorage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Core.Models;

namespace ShiftSoftware.ShiftIdentity.Blazor.Extensions;

public static class IServiceCollectionExtensions
{
    public static IServiceCollection AddShiftIdentity(this IServiceCollection services, 
        string appId, string baseUrl, string frontEndBaseUrl)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddBlazoredLocalStorage();
        services.TryAddSingleton<ShiftIdentityBlazorOptions>(x => new ShiftIdentityBlazorOptions(appId, baseUrl, frontEndBaseUrl));
        services.TryAddScoped<CodeVerifierService>();
        services.TryAddScoped<StorageService>();
        services.TryAddScoped<ShiftIdentityService>();
        services.TryAddScoped<IShiftIdentityProvider, ShiftIdentityProvider>();
        services.TryAddScoped<HttpMessageHandlerService>();
        services.AddTransient<RefreshTokenMessageHandler>();

        return services;
    }
}
