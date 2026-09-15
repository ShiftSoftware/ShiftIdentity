using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.Azure.Functions.AspNetCore.Authorization.Extensions;
using ShiftSoftware.ShiftIdentity.Data.Replication;
using System.Security.Cryptography;

namespace Microsoft.Azure.Functions.Worker;

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

        // The ShiftMapper mapper the identity catch-up replication (Replication/IdentityCatchUpReplicationExtensions)
        // maps through: ReplicateAllAsync and the per-entity ReplicateXAsync calls pass no mapping delegates, so the
        // CosmosDBReplication service resolves IShiftMapper for every document. Registered here, with identity, so a
        // Functions host that hosts identity never has to remember it. A host that does not replicate pays nothing —
        // the registration is a factory, and nothing is built until something replicates. Idempotent.
        builder.Services.AddShiftIdentityReplicationMapper();

        return builder;
    }
}
