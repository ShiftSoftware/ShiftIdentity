﻿using Microsoft.Azure.Functions.Worker;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.Azure.Functions.AspNetCore.Authorization.Extensions;
using System.Text;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Extensions;

public static class IFunctionsWorkerApplicationBuilderExtension
{
    public static IFunctionsWorkerApplicationBuilder AddShiftIdentity(this IFunctionsWorkerApplicationBuilder builder, string tokenIssuer, string tokenKey)
    {
        builder.AddAuthentication().AddJwtBearer(
            new TokenValidationParameters
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
            }
        );

        return builder;
    }
}
