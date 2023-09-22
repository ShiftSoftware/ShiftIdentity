using Microsoft.AspNetCore.Authentication.JwtBearer;
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
using System.Text;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Extensions;

public static class IMvcBuilderExtensions
{
    public static IMvcBuilder AddShiftIdentity(this IMvcBuilder builder, string tokenIssuer, string tokenKey)
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
            options.AddPolicy("ChangePassword", policy => policy.RequireClaim(ShiftIdentityClaims.ExternalToken, false.ToString().ToLower()));

            //options.AddPolicy("Scopes", policy =>
            //{
            //    policy.RequireAuthenticatedUser();
            //    policy.RequireClaim("scope", allowedScopes);
            //});
        });

        builder.AddMvcOptions(o => o.Filters.Add(new AuthorizeFilter()));

        return builder;
    }

    public static IMvcBuilder AddFakeIdentityEndPoints(this IMvcBuilder builder, TokenSettingsModel tokenConfiguration, TokenUserDataDTO userData, AppDTO app, string? userPassword, params string[] accessTrees)
    {
        builder.Services.AddSingleton<AuthCodeStoreService>();
        builder.Services.AddScoped<AuthCodeService>();
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<TokenService>();
        builder.Services.AddScoped<HashService>();

        //Fixed configurations
        var configuration = new ShiftIdentityConfiguration
        {
            Token = tokenConfiguration,
            RefreshToken = new TokenSettingsModel
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

        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton(new ShiftIdentityOptions(userData, app, accessTrees, configuration, userPassword));

        //builder.AddApplicationPart(Assembly.GetExecutingAssembly());

        builder.Services.AddScoped<IUserRepository, FakeUserRepository>();
        builder.Services.AddScoped<IAppRepository, FakeAppRepository>();
        builder.Services.AddScoped<IClaimService, FakeClaimService>();

        return builder;
    }
}
