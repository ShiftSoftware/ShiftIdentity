using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.TypeAuth.AspNetCore.Extensions;

namespace ShiftIdentity.Tests.Infrastructure;

/// <summary>
/// Real production registration/routes with only synthetic settings and an owned fixture database: the dashboard DI,
/// the attribute-driven identity CRUD routes (api/IdentityUser and the other identity entities) and every dashboard
/// endpoint, mapped the way an internal-hosting API maps them. The only mail provider is <see cref="Verifications"/>.
/// </summary>
public sealed class LegacyIdentityHttpHost<TContext> : IDisposable where TContext : ShiftIdentityDbContext
{
    private readonly TestServer server;
    public HttpClient Client { get; }
    /// <summary>The host's single ISendEmailVerification provider; records every link handed to it.</summary>
    public RecordingEmailVerification Verifications { get; } = new();
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
            // RequirePasswordChange is the configured default the form checkboxes replace; true makes an explicit
            // "false" choice observable.
            Security = new() { LoginAttemptsForLockDown = 10, LockDownInMinutes = 5, RequirePasswordChange = true },
            SASToken = new() { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), ExpiresInSeconds = 3600 },
            HashIdSettings = new() { AcceptUnencodedIds = true, UserIdsSalt = "synthetic-test", UserIdsMinHashLength = 5 },
            MfaSettings = new() { Enabled = false }, ActionTrees = []
        };
        server = new TestServer(new WebHostBuilder().UseEnvironment("Testing").ConfigureServices(services =>
        {
            services.AddRouting();
            services.AddLocalization();
            services.AddHttpContextAccessor();
            services.AddTypeAuth(o => o.AddActionTree<ShiftIdentityActions>());
            services.AddSingleton<IHashIdService>(new HashIdService(Options.Create(new ShiftEntityOptions())));
            services.AddSingleton<ISendEmailVerification>(Verifications);
            services.AddScoped(sp => (TContext)fixture.CreateContext(sp));
            var mvc = services.AddControllers();
            mvc.AddShiftEntityWeb(x => x.AddShiftIdentityDataAssembly());
            mvc.AddShiftIdentity(settings.Token.Issuer, publicKey)
                .AddShiftIdentityDashboard<TContext>(settings);
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
    public void Dispose() { Client.Dispose(); server.Dispose(); }
}

/// <summary>Stands in for the mail host: records every verification link and can fail like a broken provider.</summary>
public sealed class RecordingEmailVerification : ISendEmailVerification
{
    public List<(string Url, UserDataDTO User)> Sent { get; } = [];
    public bool Throw { get; set; }
    public Task SendEmailVerificationAsync(string url, UserDataDTO user)
    {
        Sent.Add((url, user));
        if (Throw) throw new InvalidOperationException("Synthetic mail host failure.");
        return Task.CompletedTask;
    }
}
