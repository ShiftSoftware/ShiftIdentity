using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.ShiftIdentity.AspNetCore.Fakes;
using ShiftSoftware.ShiftIdentity.AspNetCore.Models;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services.Interfaces;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.App;
using ShiftSoftware.ShiftIdentity.Core.IRepositories;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShiftIdentity(this IServiceCollection services, string tokenIssuer, string tokenRSAPublicKeyBase64,
        Type? localizationResource = null, bool setAsDefaultScheme = true)
    {
        services.AddJwtAuth(tokenIssuer, tokenRSAPublicKeyBase64, setAsDefaultScheme);

        //builder.Services.AddAuthorization(options =>
        //{
        //    options.AddPolicy("ChangePassword", policy => policy.RequireClaim(ShiftIdentityClaims.ExternalToken, false.ToString().ToLower()));

        //    //options.AddPolicy("Scopes", policy =>
        //    //{
        //    //    policy.RequireAuthenticatedUser();
        //    //    policy.RequireClaim("scope", allowedScopes);
        //    //});
        //});

        services.Configure<MvcOptions>(o => o.Filters.Add(new AuthorizeFilter()));

        // Register localizer
        if (localizationResource is null)
            services.AddTransient(x => new ShiftIdentityLocalizer(x, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        else
            services.AddTransient(x => new ShiftIdentityLocalizer(x, localizationResource));

        return services;
    }

    public static IServiceCollection AddJwtAuth(this IServiceCollection services, string tokenIssuer, string tokenRSAPublicKeyBase64, bool setAsDefaultScheme = true)
    {
        var rsa = RSA.Create();
        rsa.ImportRSAPublicKey(Convert.FromBase64String(tokenRSAPublicKeyBase64), out _);

        var authBuilder = setAsDefaultScheme
            ? services.AddAuthentication(a =>
            {
                a.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                a.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            : services.AddAuthentication();

        authBuilder.AddJwtBearer(o =>
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

        return services;
    }

    public static IServiceCollection AddFakeIdentityEndPoints(this IServiceCollection services, TokenSettingsModel tokenConfiguration, TokenUserDataDTO userData, AppDTO app, string? userPassword, params string[] accessTrees)
    {
        services.AddSingleton<AuthCodeStoreService>();
        services.AddScoped<AuthCodeService>();
        services.AddScoped<AuthService>();
        services.AddScoped<TokenService>();
        services.AddScoped<HashService>();

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
            Security = new SecuritySettingsModel
            {
                LockDownInMinutes = 0,
                LoginAttemptsForLockDown = 1000000,
                RequirePasswordChange = false
            },
            HashIdSettings = new HashIdSettings
            {
                AcceptUnencodedIds = true,
                UserIdsSalt = "k02iUHSb2ier9fiui02349AbfJEI",
                UserIdsMinHashLength = 5
            },
            IsFakeIdentity = true,
        };

        services.AddSingleton(configuration);
        services.AddSingleton(new ShiftIdentityOptions(userData, app, accessTrees, configuration, userPassword));

        //builder.AddApplicationPart(Assembly.GetExecutingAssembly());

        services.AddScoped<IUserRepository, FakeUserRepository>();
        services.AddScoped<IAppRepository, FakeAppRepository>();
        services.AddScoped<IClaimService, FakeClaimService>();

        return services;
    }
}
