using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShiftSoftware.ShiftIdentity.Blazor.AuthRefresh;
using ShiftSoftware.ShiftIdentity.Blazor.Handlers;
using ShiftSoftware.ShiftIdentity.Blazor.Providers;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftwareLocalization.Identity;

namespace ShiftSoftware.ShiftIdentity.Blazor.Extensions;

public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Registers ShiftIdentity for a standalone-WASM app using JWT in localStorage.
    /// Wires the JWT <see cref="IAuthRefreshStrategy"/> with the unified
    /// <see cref="AuthSessionService"/> + <see cref="ShiftAuthStateProvider"/> + <see cref="Auth401Handler"/>.
    /// </summary>
    public static IServiceCollection AddShiftIdentityBlazor(this IServiceCollection services,
        string appId, string baseUrl, string frontEndBaseUrl,
        ShiftIdentityHostingTypes hostingType = ShiftIdentityHostingTypes.Internal,
        Type? localizationResource = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));

        services.AddBlazoredLocalStorage();
        services.TryAddSingleton(x => new ShiftIdentityBlazorOptions(appId, baseUrl, frontEndBaseUrl)
        {
            UseCookieAuth = false,
            HostingType = hostingType,
        });
        services.TryAddScoped<IShiftIdentityProvider, ShiftIdentityProvider>();

        services.AddScoped<ShiftIdentityHttpClient>(sp =>
        {
            var options = sp.GetRequiredService<ShiftIdentityBlazorOptions>();
            return new ShiftIdentityHttpClient { BaseAddress = new Uri(options.BaseUrl) };
        });

        services.TryAddScoped<IIdentityStore, IdentityLocalStorageService>();
        services.AddScoped<MessageService>();
        services.AddTransient<TokenMessageHandler>();
        services.AddTransient<Auth401Handler>();

        AddSharedAuthRefresh<JwtRefreshStrategy>(services);
        AddLocalizer(services, localizationResource);

        services.AddAuthorizationCore();
        return services;
    }

    /// <summary>
    /// Registers ShiftIdentity for the .Client (WASM) project of a Blazor Web App with cookie auth.
    /// Wires the cookie <see cref="IAuthRefreshStrategy"/> with the unified
    /// <see cref="AuthSessionService"/> + <see cref="ShiftAuthStateProvider"/> + <see cref="Auth401Handler"/>.
    /// </summary>
    public static IServiceCollection AddShiftIdentityBlazorClient(this IServiceCollection services,
        string appId, string baseUrl, string frontEndBaseUrl,
        ShiftIdentityHostingTypes hostingType = ShiftIdentityHostingTypes.Internal,
        Type? localizationResource = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));

        services.TryAddSingleton(x => new ShiftIdentityBlazorOptions(appId, baseUrl, frontEndBaseUrl)
        {
            UseCookieAuth = true,
            HostingType = hostingType,
        });

        services.TryAddScoped<IIdentityStore, NoOpIdentityStore>();
        services.AddTransient<Auth401Handler>();

        AddSharedAuthRefresh<CookieRefreshStrategy>(services);
        AddLocalizer(services, localizationResource);

        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
        return services;
    }

    private static void AddSharedAuthRefresh<TStrategy>(IServiceCollection services)
        where TStrategy : class, IAuthRefreshStrategy
    {
        services.TryAddScoped<TStrategy>();
        services.TryAddScoped<IAuthRefreshStrategy>(sp => sp.GetRequiredService<TStrategy>());
        services.TryAddScoped<ShiftAuthStateProvider>();
        services.TryAddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<ShiftAuthStateProvider>());
        services.TryAddScoped<AuthSessionService>();
    }

    private static void AddLocalizer(IServiceCollection services, Type? localizationResource)
    {
        if (localizationResource is null)
            services.AddTransient(x => new ShiftIdentityLocalizer(x, typeof(Resource)));
        else
            services.AddTransient(x => new ShiftIdentityLocalizer(x, localizationResource));
    }

    [Obsolete("Use AddShiftIdentityBlazor instead. This method will be removed in future versions.")]
    public static IServiceCollection AddShiftIdentity(this IServiceCollection services,
        string appId, string baseUrl, string frontEndBaseUrl, Type? localizationResource = null)
    {
        services.AddShiftIdentityBlazor(appId, baseUrl, frontEndBaseUrl, localizationResource: localizationResource);
        return services;
    }
}
