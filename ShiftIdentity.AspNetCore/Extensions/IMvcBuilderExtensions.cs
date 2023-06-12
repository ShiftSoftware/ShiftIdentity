﻿using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Core.Services;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Extensions;

public static class IMvcBuilderExtensions
{
    public static IMvcBuilder AddShiftIdentity(this IMvcBuilder builder, string tokenIssuer, string tokenKey, string[] allowedScopes)
    {
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
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenKey)),
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

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("Scopes", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim("scope", allowedScopes);
            });
        });
        builder.AddMvcOptions(o => o.Filters.Add(new AuthorizeFilter("Scopes")));

        builder.Services.AddSingleton<AuthCodeStoreService>();

        ////Remove controllers of the "ShiftIdentity.Dashboard.AspNetCore" from being discovered
        //builder.PartManager.ApplicationParts.Remove(
        //    builder.PartManager.ApplicationParts.FirstOrDefault(f =>
        //        f.Name == typeof(Dashboard.AspNetCore.WebMaker).Assembly.GetName().Name)
        //    );

        //builder.PartManager.ApplicationParts.Remove(
        //    builder.PartManager.ApplicationParts.FirstOrDefault(f =>
        //        f.Name == Assembly.GetExecutingAssembly().GetName().Name)
        //    );

        return builder;
    }

    public static IMvcBuilder AddFakeIdentity(this IMvcBuilder builder, TokenSettingsModel tokenConfiguration,
        TokenUserDataDTO userData, string[] scopes,
        AppModel app, string? userPassword, params object[] accessTrees)
    {
        return builder.AddFakeIdentity(tokenConfiguration, userData, scopes, app, userPassword, accessTrees.Select(x => JsonSerializer.Serialize(x)));
    }

    public static IMvcBuilder AddFakeIdentity(this IMvcBuilder builder, TokenSettingsModel tokenConfiguration,
        TokenUserDataDTO userData, string[] scopes,
        AppModel app, string? userPassword, object accessTree)
    {
        return builder.AddFakeIdentity(tokenConfiguration, userData, scopes, app, userPassword, new string[] { JsonSerializer.Serialize(accessTree) });
    }

    public static IMvcBuilder AddFakeIdentity(this IMvcBuilder builder, TokenSettingsModel tokenConfiguration,
        TokenUserDataDTO userData, string[] scopes,
        AppModel app, string? userPassword, string accessTree)
    {
        return builder.AddFakeIdentity(tokenConfiguration, userData, scopes, app, userPassword, new string[] { accessTree });
    }

    public static IMvcBuilder AddFakeIdentity(this IMvcBuilder builder, TokenSettingsModel tokenConfiguration,
        TokenUserDataDTO userData, string[] scopes,
        AppModel app, string? userPassword, params string[] accessTrees)
    {
        //Fixed configurations
        var configuration = new ShiftIdentityConfiguration
        {
            Token = tokenConfiguration,
            RefreshToken = new TokenSettingsModel
            {
                Audience = "Shift-FakeIdentity",
                Key = "VeryStrongKeyRequiredForThisEncryption",
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
            }
        };

        builder.Services.AddSingleton(new ShiftIdentityOptions(userData, scopes, app, accessTrees, configuration, userPassword));

        builder.AddApplicationPart(Assembly.GetExecutingAssembly());

        return builder;
    }
}
