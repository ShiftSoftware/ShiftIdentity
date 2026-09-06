using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.TypeAuth.AspNetCore.Extensions;

namespace ShiftIdentity.Tests.Infrastructure;

/// <summary>Real production registration/routes with only synthetic settings and an owned fixture database.</summary>
public sealed class LegacyIdentityHttpHost<TContext> : IDisposable where TContext : ShiftIdentityDbContext
{
    private readonly TestServer server;
    public HttpClient Client { get; }
    public LegacyIdentityHttpHost(SqlIdentityFixture fixture)
    {
        using var rsa = RSA.Create();
        rsa.ImportRSAPrivateKey(fixture.Options.AccessPrivateKey, out _);
        var publicKey = Convert.ToBase64String(rsa.ExportRSAPublicKey());
        var settings = new ShiftIdentityConfiguration
        {
            Token = new() { Issuer = "https://legacy.invalid", Audience = "legacy-test", ExpireSeconds = 900,
                RSAPrivateKeyBase64 = Convert.ToBase64String(fixture.Options.AccessPrivateKey) },
            RefreshToken = new() { Issuer = "https://legacy.invalid", Audience = "legacy-refresh", ExpireSeconds = 1800,
                Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)) },
            TemporaryTokenSettings = new() { Issuer = "https://legacy.invalid", Audience = "legacy-temporary", ExpireSeconds = 300,
                Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)) },
            Security = new() { LoginAttemptsForLockDown = 10, LockDownInMinutes = 5 },
            HashIdSettings = new() { AcceptUnencodedIds = true, UserIdsSalt = "synthetic-test", UserIdsMinHashLength = 5 },
            MfaSettings = new() { Enabled = false }, ActionTrees = []
        };
        server = new TestServer(new WebHostBuilder().UseEnvironment("Testing").ConfigureServices(services =>
        {
            services.AddRouting();
            services.AddLocalization();
            services.AddHttpContextAccessor();
            services.AddTypeAuth(_ => { });
            services.AddSingleton<IHashIdService>(new HashIdService(Options.Create(new ShiftEntityOptions())));
            services.AddScoped(sp => (TContext)fixture.CreateContext(sp));
            var mvc = services.AddControllers();
            mvc.AddShiftEntityWeb(_ => { });
            mvc.AddShiftIdentity(settings.Token.Issuer, publicKey)
                .AddShiftIdentityDashboard<TContext>(settings);
        }).Configure(app =>
        {
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints => endpoints.MapShiftIdentityAuthEndpoints());
        }));
        Client = server.CreateClient();
    }
    public void Dispose() { Client.Dispose(); server.Dispose(); }
}
