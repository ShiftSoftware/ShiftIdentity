using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.AspNetCore.Endpoints;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;

namespace ShiftIdentity.Tests.Infrastructure;

/// <summary>Only an in-process HTTP fixture, using the real shared endpoints and SQL store.</summary>
public sealed class IdentityHttpHost : IDisposable
{
    private readonly TestServer server;
    public HttpClient Client { get; }
    internal IServiceProvider Services => server.Services;
    public IdentityHttpHost(SqlIdentityFixture fixture, AuthenticationClient? client = null, Action<string>? observe = null,
        params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        client ??= new("test-client", "test-api");
        using var rsa = RSA.Create();
        rsa.ImportRSAPrivateKey(fixture.Options.AccessPrivateKey, out _);
        var publicKey = new RsaSecurityKey(rsa.ExportParameters(false));
        server = new TestServer(new WebHostBuilder().UseEnvironment("Testing").ConfigureServices(services =>
        {
            AddAdmissionServices(services, fixture, client, observe, interceptors);
            services.AddAuthentication().AddJwtBearer("FixtureResource", options =>
                options.TokenValidationParameters = new()
                {
                    ValidIssuer = fixture.Options.Issuer, ValidAudience = client.Audience,
                    IssuerSigningKey = publicKey, ValidateIssuerSigningKey = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ClockSkew = TimeSpan.Zero,
                    LifetimeValidator = (nbf, exp, _, _) => nbf is not null && exp is not null &&
                        nbf <= fixture.Clock.GetUtcNow().UtcDateTime && exp > fixture.Clock.GetUtcNow().UtcDateTime
                });
            services.AddAuthorizationBuilder().AddPolicy("FixtureResource", policy =>
                policy.AddAuthenticationSchemes("FixtureResource").RequireAuthenticatedUser().RequireClaim("shift_purpose", "access"));
        }).Configure(app =>
        {
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapIdentityAdmissionEndpoints();
                endpoints.MapGet("/fixture/resource", () => Results.Ok()).RequireAuthorization("FixtureResource");
            });
        }));
        Client = server.CreateClient();
    }

    public static (string Verifier, string Challenge) Pkce()
    {
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (verifier, WebEncoders.Base64UrlEncode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier))));
    }

    // Shared test infrastructure also supports StockPlusPlus's explicitly enabled local preview.
    public static void AddAdmissionServices(IServiceCollection services, SqlIdentityFixture fixture,
        AuthenticationClient? client = null, Action<string>? observe = null,
        params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        client ??= new("test-client", "test-api");
        services.AddRouting();
        services.AddScoped(_ => fixture.CreateContext(interceptors));
        services.AddScoped<IIdentitySecurityStore, SqlIdentitySecurityStore>();
        services.AddScoped(sp => new IdentityAdmissionServices(
            sp.GetRequiredService<IIdentitySecurityStore>(), client, fixture.Options, fixture.Clock,
            new HashIdService(Options.Create(new ShiftEntityOptions())),
            fixture.Protection.CreateProtector("Identity.Totp.v2"),
            new AdmissionTokenCodec(fixture.Options, fixture.Clock), observe));
        services.AddIdentityAdmissionAuthentication();
    }

    public static void MapAdmissionEndpoints(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints) =>
        endpoints.MapIdentityAdmissionEndpoints();

    public async Task<AuthOutcome> LoginAsync(SqlIdentityFixture fixture, string challenge, string? password = null) =>
        await Read(await Client.PostAsJsonAsync("/api/identity/v2/login",
            new PasswordLoginRequest(fixture.Username, password ?? fixture.Password, challenge)));

    public async Task<AuthOutcome> CompleteAsync(string handle, string code, string verifier)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/v2/login/mfa");
        request.Headers.Authorization = new("Operation", handle);
        request.Content = JsonContent.Create(new CompleteMfaRequest(code, verifier));
        return await Read(await Client.SendAsync(request));
    }

    public async Task<AuthOutcome> RefreshAsync(string refresh) =>
        await Read(await Client.PostAsJsonAsync("/api/identity/v2/refresh", new RenewSessionRequest(refresh)));

    public Task<AuthOutcome> StartPasswordChangeAsync(string access, string challenge) =>
        SendAsync("password-change", "Bearer", access, new StartPasswordChangeRequest(challenge));
    public Task<AuthOutcome> ProvePasswordAsync(string handle, string password, string verifier) =>
        SendAsync("password-change/password", "Operation", handle, new PasswordChangeProofRequest(password, verifier));
    public Task<AuthOutcome> ChangePasswordAsync(string handle, string password, string verifier) =>
        SendAsync("password-change/complete", "Operation", handle, new CompletePasswordChangeRequest(password, verifier));
    public Task<AuthOutcome> PasswordMfaAsync(string handle, string code, string verifier) =>
        SendAsync("password-change/mfa", "Operation", handle, new CompleteMfaRequest(code, verifier));
    public Task<AuthOutcome> CancelAsync(string handle, string verifier) =>
        SendAsync("operations/cancel", "Operation", handle, new CancelOperationRequest(verifier));

    private async Task<AuthOutcome> SendAsync<T>(string route, string scheme, string credential, T body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/v2/" + route);
        request.Headers.Authorization = new(scheme, credential);
        request.Content = JsonContent.Create(body);
        return await Read(await Client.SendAsync(request));
    }

    public static async Task<AuthOutcome> Read(HttpResponseMessage response)
    {
        using (response)
            return await response.Content.ReadFromJsonAsync<AuthOutcome>() ??
                throw new InvalidOperationException("Authentication response missing.");
    }

    public void Dispose() { Client.Dispose(); server.Dispose(); }
}
