using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Helpers;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Models;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Providers;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Services;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers cookie-based authentication for Blazor Web App with ShiftIdentity.
    /// Sets up policy scheme (Cookie + JWT), cookie auth middleware, ICookieAuthManager,
    /// PersistingCookieAuthStateProvider (server-side, for SSR → WASM handoff), and ServerHttpMessageHandler.
    /// The .Client (WASM) project should call <c>AddShiftIdentityBlazorClient</c> instead.
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
        services.AddSingleton(new ShiftIdentity.Blazor.ShiftIdentityBlazorOptions(baseUrl, frontEndBaseUrl)
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
                cookieOptions.LoginPath = Constants.CookieLoginPath;
                cookieOptions.ReturnUrlParameter = Constants.ReturnUrlParameter;
                // For API requests, return raw 401/403 instead of the default HTML redirect to
                // /login or /Account/AccessDenied. The redirect is correct for SSR navigations
                // but breaks fetch/HttpClient callers that expect a status code.
                cookieOptions.Events.OnRedirectToLogin = context =>
                {
                    if (IsApiRequest(context.Request))
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    else
                        context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };
                cookieOptions.Events.OnRedirectToAccessDenied = context =>
                {
                    if (IsApiRequest(context.Request))
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    else
                        context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };
                cookieOptions.Events.OnValidatePrincipal = async context =>
                {
                    // Missing or malformed token_expires_at is treated as "refresh now" rather
                    // than silently skipped — otherwise a cookie issued by an older app version
                    // (no expiry stamp) would carry permanently stale claims until the outer
                    // cookie expired. Normal cookies always have it set (CookieAuthHelpers.cs).
                    var expiresAt = context.Properties.GetString("token_expires_at");
                    var needsRefresh = !(expiresAt != null
                        && DateTimeOffset.TryParse(expiresAt, out var expiresAtDate)
                        && expiresAtDate - DateTimeOffset.UtcNow >= TimeSpan.FromSeconds(30));

                    if (!needsRefresh)
                        return;

                    var refreshToken = context.Properties.GetString("refresh_token");
                    var authManager = context.HttpContext.RequestServices.GetRequiredService<ICookieAuthManager>();

                    TokenDTO? newToken;
                    try
                    {
                        newToken = !string.IsNullOrEmpty(refreshToken) ? await authManager.RefreshAsync(refreshToken) : null;
                    }
                    catch (Exception ex)
                    {
                        // Transient failure from the identity server (5xx, 408/429, network blip).
                        // Keep the existing principal — the cookie is still valid for up to 30 more
                        // seconds — and let the next request retry. Don't fail this request.
                        var logger = context.HttpContext.RequestServices
                            .GetService<ILogger<ICookieAuthManager>>();
                        logger?.LogWarning(ex,
                            "Cookie token refresh threw transiently; preserving session for retry.");
                        return;
                    }

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
                };
            });

        services.AddAuthorization();
        services.AddCascadingAuthenticationState();
        services.AddHttpContextAccessor();

        // Server-side state provider — reads HttpContext.User during SSR and persists claims
        // into PersistentComponentState for the WASM client to pick up on hydration.
        services.AddScoped<AuthenticationStateProvider, PersistingCookieAuthStateProvider>();

        // No-op identity store — cookie handles token storage. Stays for any consumer that
        // still injects IIdentityStore (UserAvatar, FileExplorer, etc).
        services.AddScoped<ShiftIdentity.Blazor.IIdentityStore, ShiftIdentity.Blazor.Services.NoOpIdentityStore>();

        // ServerHttpMessageHandler for forwarding cookies during SSR
        services.AddTransient<ServerHttpMessageHandler>();

        // Register correct ICookieAuthManager + ICookieLoginHandler + ICookieChangePasswordHandler
        // based on hosting type.
        if (hostingType == ShiftIdentityHostingTypes.Internal)
        {
            services.AddScoped<ICookieAuthManager, InternalCookieAuthManager>();
            services.AddScoped<ICookieLoginHandler, InternalCookieLoginHandler>();
            services.AddScoped<ICookieChangePasswordHandler, InternalCookieChangePasswordHandler>();
        }
        else
        {
            if (string.IsNullOrEmpty(baseUrl))
                throw new InvalidOperationException("baseUrl must be set for external identity hosting.");

            services.AddHttpClient("ShiftIdentityExternal", client =>
            {
                client.BaseAddress = new Uri(baseUrl);
            });
            // Eagerly construct so misconfigured JwtIssuer/JwtPublicKey fails at startup, not
            // on the first login attempt. ExternalJwtValidator gates every token returned by
            // the external identity server before its claims are bound to the local cookie.
            services.AddSingleton(new ExternalJwtValidator(options));
            services.AddScoped<ICookieAuthManager, ExternalCookieAuthManager>();
            services.AddScoped<ICookieLoginHandler, ExternalCookieLoginHandler>();
            services.AddScoped<ICookieChangePasswordHandler, ExternalCookieChangePasswordHandler>();
        }

        return services;
    }

    private static bool IsApiRequest(HttpRequest request)
    {
        if (request.Path.StartsWithSegments("/api"))
            return true;

        // Fallback for any non-/api endpoint hit by a fetch/HttpClient caller: an HTML browser
        // navigation always sends Accept: text/html, so its absence is a strong signal.
        var accept = request.Headers.Accept.ToString();
        return !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }
}
