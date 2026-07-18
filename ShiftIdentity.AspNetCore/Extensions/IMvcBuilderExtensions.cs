using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.ShiftIdentity.AspNetCore;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authorization;
using ShiftSoftware.ShiftIdentity.AspNetCore.Fakes;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services.Interfaces;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authorization;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.App;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using ShiftSoftware.ShiftIdentity.Data.IRepositories;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftwareLocalization.Identity;
using System.Security.Cryptography;


namespace Microsoft.Extensions.DependencyInjection;

public static class IMvcBuilderExtensions
{
    public static IMvcBuilder AddShiftIdentity(this IMvcBuilder builder, string tokenIssuer, string tokenRSAPublicKeyBase64,
        Type? localizationResource = null)
    {
        var rsa = RSA.Create();
        rsa.ImportRSAPublicKey(Convert.FromBase64String(tokenRSAPublicKeyBase64), out _);

        builder.Services.AddAuthentication(a =>
        {
            a.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            a.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(o =>
        {
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = false,
                ValidateIssuer = true,
                ValidIssuer = tokenIssuer,
                RequireExpirationTime = true,
                IssuerSigningKey = new RsaSecurityKey(rsa),
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                LifetimeValidator = (DateTime? notBefore, DateTime? expires, SecurityToken securityToken,
                                     TokenValidationParameters validationParameters) =>
                {
                    bool result = false;
                    var now = DateTime.UtcNow;

                    if (notBefore != null && now < notBefore)
                        result = false;

                    if (expires != null)
                        result = expires > now;

                    if (!result)
                        throw new SecurityTokenExpiredException("Token expired");

                    return result;
                }
            };

            o.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                    {
                        context.Response.Headers.Add("IS-TOKEN-EXPIRED", "true");
                    }
                    return Task.CompletedTask;
                }
            };
        });

        builder.Services.AddAuthorization();

        builder.AddMvcOptions(o => o.Filters.Add(new AuthorizeFilter()));

        // Register localizer
        if (localizationResource is null)
            builder.Services.AddTransient(x => new ShiftIdentityLocalizer(x, typeof(Resource)));
        else
            builder.Services.AddTransient(x => new ShiftIdentityLocalizer(x, localizationResource));

        return builder;
    }

    /// <summary>
    /// Wires up "step-up" authorization: a second authentication scheme for temporary (purpose-bound,
    /// pre-login) tokens, plus one authorization policy per <see cref="AuthPurpose"/> purpose so a
    /// single endpoint can serve both the enforced pre-login flow (temporary token) and the voluntary
    /// logged-in flow (access token). Annotate such endpoints with <c>[StepUp(AuthPurpose.X)]</c>.
    /// <para>
    /// Call this from identity-server hosts only (it depends on <see cref="TokenService"/> and the
    /// refresh-token key). Consumers that merely validate access tokens do not need it.
    /// </para>
    /// </summary>
    public static IMvcBuilder AddStepUpAuthorization(this IMvcBuilder builder)
    {
        // Temporary tokens are signed with the refresh-token key, so the default access-token bearer
        // scheme can't validate them. Add a dedicated scheme that does (reads the same Bearer header).
        builder.Services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, TemporaryTokenAuthenticationHandler>(
                ShiftIdentityAuthenticationSchemes.TemporaryToken, configureOptions: null);

        builder.Services.AddSingleton<IAuthorizationHandler, StepUpAuthorizationHandler>();

        builder.Services.AddAuthorization(options =>
        {
            // One policy per (purpose, allowAccessToken) combination. Adding a future flow just means
            // adding an AuthPurpose value — its policies are registered here automatically.
            //   allowAccessToken = true  → sensitive operation: full internal token OR matching purpose.
            //   allowAccessToken = false → pre-login completion: matching purpose token ONLY.
            foreach (var purpose in Enum.GetValues<AuthPurpose>())
            {
                if (purpose == AuthPurpose.None)
                    continue;

                foreach (var allowAccessToken in new[] { true, false })
                {
                    options.AddPolicy(StepUpPolicy.For(purpose, allowAccessToken), policy =>
                    {
                        policy.AddAuthenticationSchemes(
                            JwtBearerDefaults.AuthenticationScheme,
                            ShiftIdentityAuthenticationSchemes.TemporaryToken);
                        policy.RequireAuthenticatedUser();
                        policy.AddRequirements(new StepUpRequirement(purpose, allowAccessToken));
                    });
                }
            }
        });

        return builder;
    }

    /// <summary>
    /// Registers <see cref="AuthService"/> and the services it composes — token issuance, auth codes,
    /// password hashing, and TOTP. Both the identity-server host (<c>AddShiftIdentityDashboard</c>) and the
    /// dev-only fake host (<see cref="AddFakeIdentityEndPoints"/>) call this, so a dependency added to
    /// <see cref="AuthService"/>'s constructor only needs registering in one place — the two hosts can no
    /// longer drift and leave the graph unsatisfiable (which is what broke consumers when TOTP was added).
    /// </summary>
    public static IServiceCollection AddShiftIdentityAuthCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<AuthCodeStoreService>();
        services.AddScoped<AuthCodeService>();
        services.AddScoped<AuthService>();
        services.AddScoped<TokenService>();
        services.AddScoped<TotpService>();
        services.AddScoped<HashService>();

        return services;
    }

    public static IMvcBuilder AddFakeIdentityEndPoints(this IMvcBuilder builder, TokenSettingsModel tokenConfiguration, TokenUserDataDTO userData, AppDTO app, string? userPassword, params string[] accessTrees)
    {
        builder.Services.AddShiftIdentityAuthCoreServices();

        //Fixed configurations
        var configuration = new ShiftIdentityConfiguration
        {
            Token = tokenConfiguration,
            RefreshToken = new RefreshTokenSettingsModel
            {
                Audience = "Shift-FakeIdentity",
                Key = "VeryStrongKeyRequiredForThisEncryption-VeryStrongKeyRequiredForThisEncryption",
                Issuer = "Shift-FakeIdentity",
                ExpireSeconds = 31557600
            },
            TemporaryTokenSettings = new TemporaryTokenSettingsModel
            {
                Audience = "Shift-FakeIdentity-temp",
                Key = "VeryStrongKeyRequiredForThisEncryption-VeryStrongKeyRequiredForThisEncryptionTemp",
                Issuer = "Shift-FakeIdentity-Temp",
                ExpireSeconds = 3000
            },
            MfaSettings = new MfaSettingsModel
            {
                Enabled = false,
            },
            Security = new SecuritySettingsModel
            {
                LockDownInMinutes = 0,
                LoginAttemptsForLockDown = 1000000,
                RequirePasswordChange = false,
            },
            HashIdSettings = new HashIdSettings
            {
                AcceptUnencodedIds = true,
                UserIdsSalt = "k02iUHSb2ier9fiui02349AbfJEI",
                UserIdsMinHashLength = 5
            },
            IsFakeIdentity = true,
        };

        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton(new ShiftIdentityOptions(userData, app, accessTrees, configuration, userPassword));

        //builder.AddApplicationPart(Assembly.GetExecutingAssembly());

        builder.Services.AddScoped<IUserRepository, FakeUserRepository>();
        builder.Services.AddScoped<IAppRepository, FakeAppRepository>();
        builder.Services.AddScoped<IClaimService, FakeClaimService>();

        // AuthController (incl. Login/mfa) is auto-discovered here too, so the step-up scheme/policies
        // it depends on must be registered on the fake host as well.
        builder.AddStepUpAuthorization();

        return builder;
    }
}
