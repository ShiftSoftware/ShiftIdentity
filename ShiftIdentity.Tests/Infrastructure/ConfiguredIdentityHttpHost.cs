using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions;
using ShiftSoftware.TypeAuth.AspNetCore.Extensions;

namespace ShiftIdentity.Tests.Infrastructure;

/// <summary>
/// A host built the way a real identity host is built: nothing but the public dashboard registration and mapping
/// (<c>AddShiftIdentityDashboard</c>, <c>MapShiftIdentityDashboard</c>) with <c>ShiftIdentityConfiguration.Authority</c>
/// enabled, on the fixture's database and keys. Its hosted services run for real, so startup readies the policy and
/// client rows and visits every user before the first request, exactly as a deployed host does.
/// </summary>
public sealed class ConfiguredIdentityHttpHost<TContext> : IDisposable where TContext : ShiftIdentityDbContext
{
    public const string ClientId = "configured-client";
    private readonly TestServer server;
    public HttpClient Client { get; }
    internal IServiceProvider Services => server.Services;
    public ShiftIdentityConfiguration Settings { get; }

    public ConfiguredIdentityHttpHost(SqlIdentityFixture fixture, Action<ShiftIdentityConfiguration>? configure = null, bool enabled = true,
        Action<IServiceCollection>? configureServices = null)
    {
        Settings = SettingsFor(fixture, enabled);
        configure?.Invoke(Settings);
        using var rsa = RSA.Create();
        rsa.ImportRSAPrivateKey(fixture.Options.AccessPrivateKey, out _);
        var publicKey = Convert.ToBase64String(rsa.ExportRSAPublicKey());
        server = new TestServer(new WebHostBuilder().UseEnvironment("Testing").ConfigureServices(services =>
        {
            services.AddRouting();
            services.AddLocalization();
            services.AddHttpContextAccessor();
            services.AddSingleton(fixture.Clock);
            services.AddTypeAuth(o => o.AddActionTree<ShiftIdentityActions>());
            services.AddSingleton<IHashIdService>(new HashIdService(Options.Create(new ShiftEntityOptions())));
            services.AddSingleton<ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.ISecurityEmailSink>(fixture.EmailSink!);
            services.AddScoped(sp => (TContext)fixture.CreateContext(sp));
            var mvc = services.AddControllers();
            mvc.AddShiftEntityWeb(x => x.AddShiftIdentityDataAssembly());
            mvc.AddShiftIdentity(Settings.Token.Issuer, publicKey).AddShiftIdentityDashboard<TContext>(Settings);
            configureServices?.Invoke(services);
        }).Configure(app =>
        {
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapShiftEntityEndpoints<TContext>();
                endpoints.MapShiftIdentityDashboard();
            });
        }));
        Client = server.CreateClient();
    }

    /// <summary>The settings a host would configure, on the fixture's keys so its tokens validate against the fixture's options too.</summary>
    public static ShiftIdentityConfiguration SettingsFor(SqlIdentityFixture fixture, bool enabled = true) => new()
    {
        FactorProtection = fixture.FactorProtection,
        Token = new() { Issuer = fixture.Options.Issuer, Audience = "configured-api", ExpireSeconds = 900,
            RSAPrivateKeyBase64 = Convert.ToBase64String(fixture.Options.AccessPrivateKey) },
        RefreshToken = new() { Issuer = "https://legacy.invalid", Audience = "legacy-refresh", ExpireSeconds = fixture.LegacyRefreshLifetimeSeconds,
            Key = fixture.LegacyRefreshKey },
        TemporaryTokenSettings = new() { Issuer = "https://legacy.invalid", Audience = "legacy-temporary",
            ExpireSeconds = fixture.LegacyTemporaryLifetimeSeconds, Key = fixture.LegacyTemporaryKey },
        Security = new() { LoginAttemptsForLockDown = 10, LockDownInMinutes = 5, RequirePasswordChange = true },
        SASToken = new() { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), ExpiresInSeconds = 3600 },
        HashIdSettings = new() { AcceptUnencodedIds = true, UserIdsSalt = "synthetic-test", UserIdsMinHashLength = 5 },
        // The fixture's policy row: MFA enabled, not mandatory, no verified-email requirement.
        MfaSettings = new() { Enabled = true },
        ActionTrees = [],
        Authority = new AuthoritySettingsModel
        {
            Enabled = enabled, ClientId = ClientId, ClientDisplayName = "Configured identity host",
            RefreshKey = Convert.ToBase64String(fixture.Options.RefreshKey),
            OperationKey = Convert.ToBase64String(fixture.Options.OperationKey),
            RefreshLifetimeSeconds = fixture.Options.RefreshLifetimeSeconds
        }
    };

    public void Dispose() { Client.Dispose(); server.Dispose(); }
}
