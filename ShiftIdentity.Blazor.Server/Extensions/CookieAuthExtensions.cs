using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Models;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Providers;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Services;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Extensions;

public static class CookieAuthExtensions
{
    /// <summary>
    /// Registers cookie-based authentication for Blazor Web App with ShiftIdentity.
    /// Sets up policy scheme (Cookie + JWT), cookie auth middleware, ICookieAuthManager,
    /// PersistingCookieAuthStateProvider, NoOpIdentityStore, and ServerHttpMessageHandler.
    /// </summary>
    public static WebApplicationBuilder AddShiftIdentityCookieAuth(
        this WebApplicationBuilder builder,
        Action<ShiftIdentityCookieAuthOptions> configure)
    {
        var options = new ShiftIdentityCookieAuthOptions();
        configure(options);

        builder.Services.AddSingleton(options);

        // Policy scheme: Bearer token in header → JWT, otherwise → Cookies
        builder.Services.AddAuthentication("ShiftAuth")
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
                cookieOptions.Events.OnValidatePrincipal = async context =>
                {
                    var expiresAt = context.Properties.GetString("token_expires_at");
                    if (expiresAt != null && DateTimeOffset.TryParse(expiresAt, out var expiresAtDate))
                    {
                        if (expiresAtDate - DateTimeOffset.UtcNow < TimeSpan.FromSeconds(60))
                        {
                            var refreshToken = context.Properties.GetString("refresh_token");
                            if (refreshToken != null)
                            {
                                var authManager = context.HttpContext.RequestServices.GetRequiredService<ICookieAuthManager>();
                                var newToken = await authManager.RefreshAsync(refreshToken);

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
                    }
                };
            });

        builder.Services.AddAuthorization();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddHttpContextAccessor();

        // Auth state provider for SSR → WASM handoff
        builder.Services.AddScoped<AuthenticationStateProvider, PersistingCookieAuthStateProvider>();

        // No-op identity store — cookie handles token storage
        builder.Services.AddScoped<IIdentityStore, ShiftIdentity.Blazor.Services.NoOpIdentityStore>();

        // ServerHttpMessageHandler for forwarding cookies during SSR
        builder.Services.AddTransient<ServerHttpMessageHandler>();

        // Register correct ICookieAuthManager based on hosting type
        if (options.HostingType == ShiftIdentityHostingTypes.Internal)
        {
            builder.Services.AddScoped<ICookieAuthManager, InternalCookieAuthManager>();
        }
        else
        {
            if (string.IsNullOrEmpty(options.ExternalIdentityApiUrl))
                throw new InvalidOperationException("ExternalIdentityApiUrl must be set for external identity hosting.");

            builder.Services.AddHttpClient("ShiftIdentityExternal", client =>
            {
                client.BaseAddress = new Uri(options.ExternalIdentityApiUrl);
            });
            builder.Services.AddScoped<ICookieAuthManager, ExternalCookieAuthManager>();
        }

        return builder;
    }

    /// <summary>
    /// Maps the cookie auth endpoints: /api/identity/login (internal only),
    /// /api/identity/sign-in-with-token, /api/identity/refresh, /api/identity/logout.
    /// </summary>
    public static WebApplication MapShiftIdentityCookieEndpoints(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<ShiftIdentityCookieAuthOptions>();

        // Internal hosting: login via in-process AuthService
        if (options.HostingType == ShiftIdentityHostingTypes.Internal)
        {
            app.MapPost("/api/identity/login", async (
                HttpContext httpContext,
                ShiftSoftware.ShiftIdentity.AspNetCore.Services.AuthService authService,
                LoginDTO loginDto) =>
            {
                var result = await authService.LoginAsync(loginDto);

                if (result.Result != ShiftSoftware.ShiftIdentity.AspNetCore.Models.LoginResultEnum.Success)
                {
                    return Results.BadRequest(new { error = result.ErrorMessage });
                }

                await CookieAuthHelpers.SignInWithToken(httpContext, result.Token);

                return Results.Ok(new
                {
                    result.Token.UserData,
                    result.Token.RequirePasswordChange,
                });
            }).AllowAnonymous();
        }

        // Sign in with an externally-obtained JWT token
        app.MapPost("/api/identity/sign-in-with-token", async (
            HttpContext httpContext,
            ShiftIdentityCookieAuthOptions cookieAuthOptions,
            SignInWithTokenRequest request) =>
        {
            var issuer = cookieAuthOptions.JwtIssuer;
            var publicKeyBase64 = cookieAuthOptions.JwtPublicKeyBase64;

            if (string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(publicKeyBase64))
                return Results.Problem("Token validation is not configured.");

            var rsa = RSA.Create();
            rsa.ImportRSAPublicKey(Convert.FromBase64String(publicKeyBase64), out _);
            var rsaSecurityKey = new RsaSecurityKey(rsa);

            var tokenHandler = new JwtSecurityTokenHandler();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = rsaSecurityKey,
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = false,
                ValidateLifetime = true,
            };

            try
            {
                tokenHandler.ValidateToken(request.Token, validationParameters, out _);
            }
            catch (SecurityTokenException)
            {
                return Results.Unauthorized();
            }

            var token = new TokenDTO
            {
                Token = request.Token,
                RefreshToken = request.RefreshToken,
                TokenLifeTimeInSeconds = request.TokenLifeTimeInSeconds,
            };

            await CookieAuthHelpers.SignInWithToken(httpContext, token);
            return Results.Ok();
        }).AllowAnonymous();

        // Refresh the auth cookie
        app.MapPost("/api/identity/refresh", async (HttpContext httpContext, ICookieAuthManager authManager) =>
        {
            var authResult = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            var refreshToken = authResult.Properties?.GetString("refresh_token");

            if (refreshToken == null)
                return Results.Unauthorized();

            var newToken = await authManager.RefreshAsync(refreshToken);
            if (newToken == null)
                return Results.Unauthorized();

            await CookieAuthHelpers.SignInWithToken(httpContext, newToken);

            return Results.Ok(new { newToken.UserData });
        });

        // Logout
        app.MapPost("/api/identity/logout", async (HttpContext httpContext) =>
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        }).AllowAnonymous();

        return app;
    }
}

internal static class CookieAuthHelpers
{
    internal static async Task SignInWithToken(HttpContext httpContext, TokenDTO token)
    {
        var claims = BuildClaimsFromToken(token);
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            AllowRefresh = true,
        };
        authProperties.SetString("refresh_token", token.RefreshToken);
        authProperties.SetString("token_expires_at",
            DateTimeOffset.UtcNow.AddSeconds(token.TokenLifeTimeInSeconds ?? 900).ToString("o"));

        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);
    }

    internal static List<Claim> BuildClaimsFromToken(TokenDTO token)
    {
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token.Token);

        var claims = new List<Claim>();

        foreach (var claim in jwt.Claims)
        {
            if (claim.Type is "nbf" or "exp" or "iat" or "iss" or "aud")
                continue;

            claims.Add(new Claim(claim.Type, claim.Value));
        }

        return claims;
    }
}
