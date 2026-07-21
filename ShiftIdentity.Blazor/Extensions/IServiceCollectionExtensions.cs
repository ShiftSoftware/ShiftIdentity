using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http;
using System.Net.Http.Headers;
using ShiftSoftware.ShiftIdentity.Blazor.Handlers;
using ShiftSoftware.ShiftIdentity.Blazor.Providers;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftwareLocalization.Identity;

namespace ShiftSoftware.ShiftIdentity.Blazor.Extensions;

public static class IServiceCollectionExtensions
{
    public static IServiceCollection AddShiftIdentity(this IServiceCollection services,
        string appId, string baseUrl, string frontEndBaseUrl, bool noNeedAuthCode = false, Type? localizationResource = null,
        Action<ShiftIdentityBlazorOptions>? configure = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // Built up front rather than in a factory, because the token store registration below depends on it.
        var options = new ShiftIdentityBlazorOptions(appId, baseUrl, frontEndBaseUrl, noNeedAuthCode);
        configure?.Invoke(options);

        services.AddBlazoredLocalStorage();
        services.TryAddSingleton(options);
        services.TryAddScoped<CookieService>();
        services.TryAddScoped<CodeVerifierService>();
        services.TryAddScoped<ShiftIdentityService>();
        services.TryAddScoped<IShiftIdentityProvider, ShiftIdentityProvider>();
        services.TryAddScoped<TokenRefreshService>();

        // Register a dedicated HttpClient for HttpMessageHandlerService to avoid DI loop
        services.AddScoped<ShiftIdentityHttpClient>(sp =>
        {
            var options = sp.GetRequiredService<ShiftIdentityBlazorOptions>();
            return new ShiftIdentityHttpClient { BaseAddress = new Uri(options.BaseUrl) };
        });
        services.TryAddScoped<HttpMessageHandlerService>();

        services.AddTransient<TokenMessageHandlerWithAutoRefresh>();

        if (options.RefreshTokenStorage == RefreshTokenStorage.Cookie)
            services.TryAddScoped<IIdentityStore, IdentityCookieStore>();
        else
            services.TryAddScoped<IIdentityStore, IdentityLocalStorageService>();

        services.AddScoped<MessageService>();
        services.AddScoped<CodeVerifierStorageService>();

        services.TryAddScoped<AuthenticationStateProvider, ShiftIdentityAuthStateProvider>();
        services.AddTransient<TokenMessageHandler>();
        services.AddAuthorizationCore();

        // Register localizer
        if (localizationResource is null)
            services.AddTransient(x => new ShiftIdentityLocalizer(x, typeof(Resource)));
        else
            services.AddTransient(x => new ShiftIdentityLocalizer(x, localizationResource));

        return services;
    }
}
