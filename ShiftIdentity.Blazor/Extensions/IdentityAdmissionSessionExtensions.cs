using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShiftSoftware.ShiftIdentity.Blazor.Providers;
using ShiftSoftware.ShiftIdentity.Blazor.Services;

namespace ShiftSoftware.ShiftIdentity.Blazor.Extensions;

public static class IdentityAdmissionSessionExtensions
{
    /// <summary>
    /// Selects v2 renewal for a staged authority. Supply a dedicated HTTP client without a bearer handler.
    /// A storage key selects isolated browser persistence; otherwise register persistence before calling this,
    /// or use the default scoped in-memory storage for server circuits.
    /// </summary>
    public static IServiceCollection AddIdentityAdmissionSession(this IServiceCollection services,
        Func<IServiceProvider, HttpClient> http, string? storageKey = null)
    {
        if (storageKey is not null)
        {
            services.AddBlazoredLocalStorage();
            services.Replace(ServiceDescriptor.Scoped<IIdentityTokenStorage>(sp => new IdentityLocalStorageService(
                sp.GetRequiredService<ILocalStorageService>(), sp.GetRequiredService<ISyncLocalStorageService>(), storageKey)));
        }
        else services.TryAddScoped<IIdentityTokenStorage, InMemoryIdentityTokenStorage>();

        services.Replace(ServiceDescriptor.Scoped<IdentityRenewalTransport>(sp => new AdmissionIdentityRenewalTransport(http(sp))));
        services.TryAddScoped(sp => new IdentitySession(sp.GetRequiredService<IIdentityTokenStorage>(),
            sp.GetRequiredService<IdentityRenewalTransport>()));
        services.Replace(ServiceDescriptor.Scoped<AuthenticationStateProvider, ShiftIdentityAuthStateProvider>());
        services.AddAuthorizationCore();
        return services;
    }

    /// <summary>Renews through the session's registered transport, sharing any renewal already in progress.</summary>
    public static Task<bool> RenewAsync(this IdentitySession session) => session.RenewAsync();
}
