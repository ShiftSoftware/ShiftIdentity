using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Helpers;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Models;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Providers;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Services;
using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers cookie-based authentication for Blazor Web App with ShiftIdentity.
    /// Sets up policy scheme (Cookie + JWT), cookie auth middleware, ICookieAuthManager,
    /// PersistingCookieAuthStateProvider, NoOpIdentityStore, and ServerHttpMessageHandler.
    /// </summary>
    public static IServiceCollection AddShiftIdentityBlazorServer(
        this IServiceCollection services,
        string appId, string baseUrl, string frontEndBaseUrl,
        ShiftIdentityHostingTypes hostingType,
        Action<ShiftIdentityCookieAuthOptions> configure)
    {
        var options = new ShiftIdentityCookieAuthOptions();
        configure(options);

        services.AddSingleton(options);
        services.AddSingleton(new ShiftIdentityBlazorOptions(appId, baseUrl, frontEndBaseUrl)
        {
            UseCookieAuth = true,
            HostingType = hostingType,
        });

        // Policy scheme: Bearer token in header → JWT, otherwise → Cookies
        services.AddAuthentication("ShiftAuth")
            .AddPolicyScheme("ShiftAuth", "Cookie or JWT", policyOptions =>
            {
                policyOptions.ForwardDefaultSelector = context =>
                {
                    var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
                    if (authHeader?.StartsWith("Bearer ") == true)
                        return Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
                    return CookieAuthenticationDefaults.AuthenticationScheme;
                };
            })
            .AddCookie(cookieOptions =>
            {
                cookieOptions.CookieManager = new ChunkingCookieManager();
                cookieOptions.Cookie.HttpOnly = true;
                cookieOptions.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                cookieOptions.Cookie.SameSite = SameSiteMode.Lax;
                cookieOptions.Cookie.Name = options.CookieName;
                cookieOptions.ExpireTimeSpan = options.ExpireTimeSpan;
                cookieOptions.SlidingExpiration = true;
                cookieOptions.LoginPath = options.LoginPath;
                cookieOptions.ReturnUrlParameter = Constants.ReturnUrlParameter;
                cookieOptions.Events.OnValidatePrincipal = async context =>
                {
                    var expiresAt = context.Properties.GetString("token_expires_at");
                    if (expiresAt != null && DateTimeOffset.TryParse(expiresAt, out var expiresAtDate))
                    {
                        // refresh the token if it's expiring within 30 seconds
                        if (expiresAtDate - DateTimeOffset.UtcNow < TimeSpan.FromSeconds(30))
                        {
                            var refreshToken = context.Properties.GetString("refresh_token");
                            var authManager = context.HttpContext.RequestServices.GetRequiredService<ICookieAuthManager>();
                            var newToken = refreshToken != null ? await authManager.RefreshAsync(refreshToken) : null;

                            if (newToken != null)
                            {
                                var claims = CookieAuthHelpers.BuildClaimsFromToken(newToken);
                                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                                context.ReplacePrincipal(new ClaimsPrincipal(identity));

                                context.Properties.SetString("refresh_token", newToken.RefreshToken);
                                context.Properties.SetString("token_expires_at",
                                    DateTimeOffset.UtcNow.AddSeconds(newToken.TokenLifeTimeInSeconds ?? 900).ToString("o"));

                                context.ShouldRenew = true;
                            }
                            else
                            {
                                context.RejectPrincipal();
                                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                            }
                        }
                    }
                };
            });

        services.AddAuthorization();
        services.AddCascadingAuthenticationState();
        services.AddHttpContextAccessor();

        // Auth state provider for SSR → WASM handoff
        services.AddScoped<AuthenticationStateProvider, PersistingCookieAuthStateProvider>();

        // No-op identity store — cookie handles token storage
        services.AddScoped<IIdentityStore, ShiftIdentity.Blazor.Services.NoOpIdentityStore>();

        // ServerHttpMessageHandler for forwarding cookies during SSR
        services.AddTransient<ServerHttpMessageHandler>();

        // Register correct ICookieAuthManager based on hosting type
        if (hostingType == ShiftIdentityHostingTypes.Internal)
        {
            services.AddScoped<ICookieAuthManager, InternalCookieAuthManager>();
        }
        else
        {
            if (string.IsNullOrEmpty(baseUrl))
                throw new InvalidOperationException("baseUrl must be set for external identity hosting.");

            services.AddHttpClient("ShiftIdentityExternal", client =>
            {
                client.BaseAddress = new Uri(baseUrl);
            });
            services.AddScoped<ICookieAuthManager, ExternalCookieAuthManager>();
        }

        return services;
    }
}
