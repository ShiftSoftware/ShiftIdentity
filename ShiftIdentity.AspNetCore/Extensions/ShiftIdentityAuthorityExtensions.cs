using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.AspNetCore.Endpoints;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Data.Authentication;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ShiftIdentityAuthorityServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the identity authority for this host: the SQL security store on the host's scoped
        /// <c>ShiftIdentityDbContext</c>, the admission services, the operation credential scheme and policies, the
        /// startup job that readies the policy and client rows, the factor visitor and the maintenance loop, and the
        /// repository authority behind the legacy administrator writers. <c>AddShiftIdentityDashboard</c> calls this
        /// when <c>ShiftIdentityConfiguration.Authority.Enabled</c> is true; call it directly only from a host that
        /// registers the dashboard services another way. Requires the host's <c>IHashIdService</c>. The configuration
        /// is validated here, so a misconfigured host fails at startup rather than at the first login.
        /// </summary>
        public static IServiceCollection AddShiftIdentityAuthority(this IServiceCollection services, ShiftIdentityConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);
            if (services.Any(x => x.ServiceType == typeof(IdentityAuthorityRegistration))) return services;
            var registration = IdentityAuthorityRegistration.Create(configuration);
            services.AddSingleton(registration);
            services.TryAddSingleton(configuration);
            services.TryAddScoped<ISecurityEmailSink, HostSecurityEmailSink>();
            services.AddScoped<IIdentitySecurityStore, SqlIdentitySecurityStore>();
            services.AddScoped(sp =>
            {
                var current = sp.GetRequiredService<IdentityAuthorityRegistration>();
                var clock = sp.GetService<TimeProvider>() ?? TimeProvider.System;
                var options = current.Options;
                return new IdentityAdmissionServices(sp.GetRequiredService<IIdentitySecurityStore>(), current.Client, options, clock,
                    sp.GetRequiredService<IHashIdService>(), sp.GetRequiredService<IdentityMaterialProtector>(),
                    new AdmissionTokenCodec(options, clock))
                {
                    // Resolved when a security email is sent, not here: a host sender that cannot be built without a
                    // connection or setting must fail that send only, never sign-in or anything else these services run.
                    EmailSink = sp.GetRequiredService<IServiceProviderIsService>().IsService(typeof(ISecurityEmailSink))
                        ? new DeferredSecurityEmailSink(sp) : null,
                    EmailVerificationRedirectUrl = configuration.EmailVerificationRedirectUrl,
                    LegacyRefreshTokens = new LegacyRefreshTokenCodec(configuration.RefreshToken, clock),
                    LegacyTemporaryTokens = LegacyTemporaryTokenCodec.TryCreate(configuration.TemporaryTokenSettings, clock)
                };
            });
            // Hosted services start in registration order: the policy and client rows must exist before the factor
            // visitor admits the first user.
            services.AddHostedService<IdentityAuthorityStartup>();
            services.AddIdentityAdmissionAuthentication();
            return services;
        }
    }
}

namespace Microsoft.AspNetCore.Builder
{
    public static class ShiftIdentityAuthorityEndpointRouteBuilderExtensions
    {
        /// <summary>
        /// Maps the authority's routes under <c>api/identity/v2</c> when this host registered the authority; maps
        /// nothing otherwise. <c>MapShiftIdentityDashboard</c> calls this.
        /// </summary>
        public static IEndpointRouteBuilder MapShiftIdentityAuthority(this IEndpointRouteBuilder endpoints)
        {
            ArgumentNullException.ThrowIfNull(endpoints);
            if (endpoints.ServiceProvider.GetService<IdentityAuthorityRegistration>() is not null)
                endpoints.MapIdentityAdmissionEndpoints();
            return endpoints;
        }
    }
}
