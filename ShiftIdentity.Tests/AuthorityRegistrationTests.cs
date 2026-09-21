using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Services;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// How a host turns the authority on: ShiftIdentityConfiguration.Authority through the ordinary dashboard registration.
/// Off, nothing is registered or mapped; on, the configuration is validated at registration and the services and
/// routes exist. No database is involved here.
/// </summary>
[Trait("Category", "Policy")]
public sealed class AuthorityRegistrationTests
{
    private static readonly string PrivateKey;
    private static readonly string PublicKey;
    private static readonly string RefreshKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    private static readonly string OperationKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    static AuthorityRegistrationTests()
    {
        using var rsa = RSA.Create(2048);
        PrivateKey = Convert.ToBase64String(rsa.ExportRSAPrivateKey());
        PublicKey = Convert.ToBase64String(rsa.ExportRSAPublicKey());
    }

    private static ShiftIdentityConfiguration Valid(bool enabled = true) => new()
    {
        Token = new() { Issuer = "https://identity.invalid", Audience = "configured-api", ExpireSeconds = 600, RSAPrivateKeyBase64 = PrivateKey },
        RefreshToken = new() { Issuer = "https://identity.invalid", Audience = "configured-refresh", ExpireSeconds = 3600, Key = "legacy-refresh-key-" + new string('x', 60) },
        TemporaryTokenSettings = new() { Issuer = "https://identity.invalid", Audience = "temporary", ExpireSeconds = 300, Key = "legacy-temporary-key-" + new string('y', 60) },
        Security = new(), SASToken = new() { Key = "sas", ExpiresInSeconds = 3600 },
        HashIdSettings = new() { AcceptUnencodedIds = true, UserIdsSalt = "synthetic", UserIdsMinHashLength = 5 },
        MfaSettings = new() { Enabled = true, Mandatory = false }, ActionTrees = [],
        FactorProtection = new() { ActiveKeyId = "k1", Keys = new() { ["k1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) } },
        Authority = new() { Enabled = enabled, ClientId = "identity-host", RefreshKey = RefreshKey, OperationKey = OperationKey, RequireVerifiedEmail = true }
    };

    [Fact]
    public void Options_come_from_the_configured_token_settings_and_the_authority_keys()
    {
        var registration = IdentityAuthorityRegistration.Create(Valid());
        var options = registration.Options;
        Assert.Equal("https://identity.invalid", options.Issuer);
        Assert.Equal("configured-refresh", options.RefreshAudience);
        Assert.Equal(Convert.FromBase64String(PrivateKey), options.AccessPrivateKey);
        Assert.Equal(Convert.FromBase64String(RefreshKey), options.RefreshKey);
        Assert.Equal(Convert.FromBase64String(OperationKey), options.OperationKey);
        Assert.Equal(600, options.AccessLifetimeSeconds);
        Assert.Equal(3600, options.RefreshLifetimeSeconds);
        Assert.Equal(72000, options.AdministratorAuthenticationGraceSeconds);
        Assert.Equal(1, options.PolicyRevision);
        Assert.Equal(new AuthenticationClient("identity-host", "configured-api"), registration.Client);
        Assert.True(registration.MfaEnabled); Assert.False(registration.MfaMandatory); Assert.True(registration.RequireVerifiedEmail);
        Assert.Equal("identity-host", registration.ClientDisplayName);
        Assert.Equal("/", registration.RedirectUri);
    }

    [Fact]
    public void Explicit_lifetimes_audiences_and_names_override_the_derived_ones()
    {
        var configuration = Valid();
        configuration.FrontEndUrl = "https://identity.invalid/app/";
        configuration.Authority.AccessLifetimeSeconds = 300; configuration.Authority.RefreshLifetimeSeconds = 60;
        configuration.Authority.AdministratorAuthenticationGraceSeconds = 120;
        configuration.Authority.Audience = "explicit-api"; configuration.Authority.RefreshAudience = "explicit-refresh";
        configuration.Authority.ClientDisplayName = "  Identity  "; configuration.Authority.ClientId = " identity-host ";
        var registration = IdentityAuthorityRegistration.Create(configuration);
        Assert.Equal(300, registration.Options.AccessLifetimeSeconds); Assert.Equal(60, registration.Options.RefreshLifetimeSeconds);
        Assert.Equal(120, registration.Options.AdministratorAuthenticationGraceSeconds);
        Assert.Equal("explicit-refresh", registration.Options.RefreshAudience);
        Assert.Equal(new AuthenticationClient("identity-host", "explicit-api"), registration.Client);
        Assert.Equal("Identity", registration.ClientDisplayName);
        Assert.Equal("https://identity.invalid/app/", registration.RedirectUri);
    }

    [Fact]
    public void Text_keys_are_accepted_when_long_enough_and_read_as_utf8()
    {
        // A key is Base64 when it parses as Base64; these do not, so they are the UTF-8 bytes of the text.
        var configuration = Valid();
        configuration.Authority.RefreshKey = "Please-Change-This-Authority-RefreshKey:" + new string('r', 40);
        configuration.Authority.OperationKey = "Please-Change-This-Authority-OperationKey:" + new string('o', 8);
        var options = IdentityAuthorityRegistration.Create(configuration).Options;
        Assert.Equal(Encoding.UTF8.GetBytes(configuration.Authority.RefreshKey), options.RefreshKey);
        Assert.Equal(Encoding.UTF8.GetBytes(configuration.Authority.OperationKey), options.OperationKey);
    }

    public static TheoryData<string, Action<ShiftIdentityConfiguration>> Invalid => new()
    {
        { "Authority.ClientId", c => c.Authority.ClientId = " " },
        { "Authority.RefreshKey", c => c.Authority.RefreshKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(63)) },
        { "RefreshToken.Key", c => { c.Authority.RefreshKey = null; c.RefreshToken.Key = "short"; } },
        { "RefreshToken.Key", c => { c.Authority.RefreshKey = null; c.RefreshToken.Key = " "; } },
        { "Authority.OperationKey", c => c.Authority.OperationKey = "short" },
        { "Authority.AccessLifetimeSeconds", c => c.Token.ExpireSeconds = 3600 },
        { "Authority.AccessLifetimeSeconds", c => c.Authority.AccessLifetimeSeconds = 901 },
        { "Authority.RefreshLifetimeSeconds", c => { c.RefreshToken.ExpireSeconds = 0; c.Authority.RefreshLifetimeSeconds = null; } },
        { "Authority.AdministratorAuthenticationGraceSeconds", c => c.Authority.AdministratorAuthenticationGraceSeconds = 0 },
        { "Authority.AdministratorAuthenticationGraceSeconds", c => c.Authority.AdministratorAuthenticationGraceSeconds = -1 },
        { "Token.RSAPrivateKeyBase64", c => c.Token.RSAPrivateKeyBase64 = PublicKey },
        { "Token.Issuer", c => c.Token.Issuer = "" },
        { "FactorProtection", c => c.FactorProtection = new() },
        { "Authority.Enabled", c => c.Authority.Enabled = false },
        { "EmailVerificationRedirectUrl", c => c.EmailVerificationRedirectUrl = "/caller/path" },
        { "EmailVerificationRedirectUrl", c => c.EmailVerificationRedirectUrl = "javascript:alert(1)" },
        { "EmailVerificationRedirectUrl", c => c.EmailVerificationRedirectUrl = "https://user:pass@example.invalid/" }
    };

    [Theory, MemberData(nameof(Invalid))]
    public void A_misconfigured_authority_is_refused_at_registration_naming_the_setting(string setting, Action<ShiftIdentityConfiguration> corrupt)
    {
        var configuration = Valid();
        corrupt(configuration);
        var error = Assert.Throws<InvalidOperationException>(() => IdentityAuthorityRegistration.Create(configuration));
        Assert.Contains("ShiftIdentityConfiguration." + setting, error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Omitted_authority_refresh_key_reuses_the_exact_existing_utf8_bytes(bool base64Text)
    {
        var configuration = Valid();
        configuration.Authority.RefreshKey = null;
        configuration.RefreshToken.Key = base64Text
            ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            : "  existing-refresh-secret:" + new string('x', 64) + "  ";
        var options = IdentityAuthorityRegistration.Create(configuration).Options;
        Assert.Equal(Encoding.UTF8.GetBytes(configuration.RefreshToken.Key), options.RefreshKey);
        Assert.Equal(Convert.FromBase64String(OperationKey), options.OperationKey);
    }

    [Fact]
    public void An_explicit_refresh_key_may_match_the_existing_issuer()
    {
        var configuration = Valid();
        var shared = new RefreshTokenSettingsModel
        {
            Issuer = configuration.RefreshToken.Issuer, Audience = "shared-key-refresh",
            Key = "shared-refresh-secret:" + new string('s', 64), ExpireSeconds = 3600
        };
        configuration.RefreshToken = shared;
        configuration.Authority.RefreshKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(shared.Key));
        Assert.Equal(Encoding.UTF8.GetBytes(shared.Key), IdentityAuthorityRegistration.Create(configuration).Options.RefreshKey);
    }

    [Fact]
    public void The_dashboard_registration_registers_nothing_of_the_authority_while_it_is_off()
    {
        var services = Dashboard(Valid(enabled: false));
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IdentityAuthorityRegistration));
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IdentityAdmissionServices));
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IIdentitySecurityStore));
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IUserAccountAuthority));
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IHostedService) &&
            x.ImplementationType is var job && (job == typeof(IdentityAuthorityStartup) || job == typeof(LegacyTotpMigration) || job == typeof(AdmissionMaintenance)));
    }

    [Fact]
    public void The_dashboard_registration_registers_the_authority_and_its_startup_order_when_it_is_on()
    {
        var services = Dashboard(Valid());
        Assert.Single(services, x => x.ServiceType == typeof(IdentityAuthorityRegistration));
        Assert.Single(services, x => x.ServiceType == typeof(IdentityAdmissionServices) && x.Lifetime == ServiceLifetime.Scoped);
        Assert.Single(services, x => x.ServiceType == typeof(IIdentitySecurityStore) && x.ImplementationType == typeof(SqlIdentitySecurityStore));
        Assert.Single(services, x => x.ServiceType == typeof(IUserAccountAuthority));
        Type[] authorityJobs = [typeof(IdentityAuthorityStartup), typeof(LegacyTotpMigration), typeof(AdmissionMaintenance)];
        var hosted = services.Where(x => x.ServiceType == typeof(IHostedService) && authorityJobs.Contains(x.ImplementationType))
            .Select(x => x.ImplementationType).ToArray();
        // Hosted services start in registration order: the policy and client rows are readied before the visitor admits the first user.
        Assert.Equal(authorityJobs, hosted);
        // Registering twice (a host that also calls AddShiftIdentityAuthority itself) adds nothing.
        services.AddShiftIdentityAuthority(Valid());
        Assert.Single(services, x => x.ServiceType == typeof(IdentityAuthorityRegistration));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_v2_routes_are_mapped_only_while_the_authority_is_on(bool enabled)
    {
        var configuration = Valid(enabled);
        using var server = new TestServer(new WebHostBuilder().UseEnvironment("Testing").ConfigureServices(services =>
        {
            services.AddRouting();
            services.AddSingleton<IHashIdService>(new HashIdService(Options.Create(new ShiftEntityOptions())));
            services.AddDbContext<IdentityTestDbContext>(o => o.UseSqlServer("Server=(none);Connect Timeout=1"));
            services.AddScoped<ShiftSoftware.ShiftIdentity.Data.ShiftIdentityDbContext>(sp => sp.GetRequiredService<IdentityTestDbContext>());
            if (enabled) services.AddShiftIdentityAuthority(configuration);
            // Startup readiness needs a database; this checks the mapping alone.
            services.RemoveAll<IHostedService>();
        }).Configure(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints => endpoints.MapShiftIdentityAuthority());
        }));
        using var response = await server.CreateClient().GetAsync("/api/identity/v2/mfa");
        // Without a bearer the route answers with a refusal, not with 404; and it does that before touching the store.
        Assert.Equal(enabled ? HttpStatusCode.BadRequest : HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_host_sink_overrides_the_adapter_before_or_after_authority_registration(bool before)
    {
        var services = new ServiceCollection();
        var inbox = new LocalSecurityInbox();
        if (before) services.AddSingleton<ISecurityEmailSink>(inbox);
        services.AddShiftIdentityAuthority(Valid());
        if (!before) services.AddSingleton<ISecurityEmailSink>(inbox);
        using var provider = services.BuildServiceProvider();
        Assert.Same(inbox, provider.GetRequiredService<ISecurityEmailSink>());
        // A custom sink does not need any legacy provider or a front-end URL.
        IdentityAuthorityStartup.CheckAdapters(provider);
    }

    private static ServiceCollection Dashboard(ShiftIdentityConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHashIdService>(new HashIdService(Options.Create(new ShiftEntityOptions())));
        services.AddControllers().AddShiftIdentity(configuration.Token.Issuer, PublicKey).AddShiftIdentityDashboard<IdentityTestDbContext>(configuration);
        return services;
    }
}
