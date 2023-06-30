using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShiftSoftware.ShiftIdentity.Blazor.Handlers;
//using ShiftSoftware.ShiftIdentity.Blazor.Handlers;
using ShiftSoftware.ShiftIdentity.Blazor.Providers;
using ShiftSoftware.ShiftIdentity.Blazor.Services;

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
        services.TryAddScoped<ShiftIdentityService>();
        services.TryAddScoped<IShiftIdentityProvider, ShiftIdentityProvider>();
        services.TryAddScoped<HttpMessageHandlerService>();
        services.AddTransient<TokenMessageHandlerWithAutoRefresh>();
        services.TryAddScoped<IIdentityStore, IdentityLocalStorageService>();

        services.TryAddScoped<AuthenticationStateProvider, ShiftIdentityAuthStateProvider>();
        services.AddTransient<TokenMessageHandler>();
        services.AddAuthorizationCore();


        return services;
    }
}
