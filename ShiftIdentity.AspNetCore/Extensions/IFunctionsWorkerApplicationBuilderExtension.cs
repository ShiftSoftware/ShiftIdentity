using Microsoft.Azure.Functions.Worker;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.Azure.Functions.AspNetCore.Authorization.Extensions;
using System.Security.Cryptography;
using System.Text;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Extensions;

public static class IFunctionsWorkerApplicationBuilderExtension
{
    public static IFunctionsWorkerApplicationBuilder AddShiftIdentity(this IFunctionsWorkerApplicationBuilder builder, string tokenIssuer, string tokenRSAPublicKeyBase64, bool validateTokenLifeTime = true)
    {
        var rsa = RSA.Create();
        rsa.ImportRSAPublicKey(Convert.FromBase64String(tokenRSAPublicKeyBase64), out _);

        var o = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = tokenIssuer,
            IssuerSigningKey = new RsaSecurityKey(rsa),
            ValidateIssuerSigningKey = true,
            ValidateAudience = false,
            ValidateLifetime = false,
            RequireExpirationTime = false,
        };

        if (validateTokenLifeTime)
        {
            o.ValidateLifetime = true;
            o.RequireExpirationTime = true;
            o.ClockSkew = TimeSpan.Zero;
            o.LifetimeValidator = (DateTime? notBefore, DateTime? expires, SecurityToken securityToken,
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
            };
        }

        builder.AddAuthentication().AddJwtBearer(o);

        return builder;
    }
}
