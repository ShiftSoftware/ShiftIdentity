using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using ShiftIdentity.DevHost;
using ShiftIdentity.Tests.Infrastructure;

// A local identity host for hand review. See the project file for what it is and what it is not.
var options = DevHostOptions.Parse(args);

await using var fixture = new SqlIdentityFixture();
await fixture.InitializeAsync();
await DevDatabases.MarkAsync(fixture);
var abandoned = await DevDatabases.DropAbandonedAsync(fixture);
var accounts = await DevAccounts.SeedAsync(fixture);

var port = options.Port == 0 ? DevHostOptions.FreeLoopbackPort() : options.Port;
var loopbackOrigin = $"http://localhost:{port}";
var lanOrigin = options.LanAddress is { } lan ? $"http://{lan}:{port}" : null;
// The address a phone reaches: the tunnel's, else the LAN address when the host binds one, else loopback (a second
// browser tab on this machine).
var publicOrigin = options.TunnelOrigin ?? lanOrigin ?? loopbackOrigin;
var origins = new[] { loopbackOrigin, lanOrigin, options.TunnelOrigin }.OfType<string>().ToArray();
// The host names a direct request may carry. A request through the tunnel is recognised by the guard instead.
var hosts = new[] { $"localhost:{port}", $"127.0.0.1:{port}", $"[::1]:{port}", lanOrigin is null ? null : new Uri(lanOrigin).Authority }.OfType<string>();

var settings = ConfiguredIdentityHttpHost<IdentityTestDbContext>.SettingsFor(fixture);
settings.FrontEndUrl = publicOrigin;
settings.Authority.ClientId = "identity-devhost";
settings.Authority.ClientDisplayName = "Identity DevHost";
settings.Authority.AccessLifetimeSeconds = options.AccessLifetimeSeconds;
// Device sign-in: the simulator's screens, and the phone page at the address a phone can reach.
settings.Authority.DeviceClients = new() { ["service-screen"] = "Service Screen", ["lab-screen"] = "Lab Screen" };
settings.Authority.DeviceVerificationUri = publicOrigin + "/Identity/device";

var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
{
    ApplicationName = typeof(DevHostOptions).Assembly.GetName().Name,
    EnvironmentName = Environments.Development
});
builder.WebHost.UseKestrel(kestrel =>
{
    kestrel.ListenLocalhost(port);
    if (options.LanAddress is { } address) kestrel.Listen(address, port);
});
builder.WebHost.UseStaticWebAssets();
builder.Services.AddLogging(logging => logging.AddConsole()
    .AddFilter("Microsoft.AspNetCore", LogLevel.Warning)
    .AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning));
// Keys for this run only. Nothing is written to the machine's key ring.
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
ConfiguredIdentityHttpHost<IdentityTestDbContext>.AddHostServices(builder.Services, fixture, settings);
builder.Services.AddSingleton(new DevHostState(fixture, accounts, origins, publicOrigin));

await using var app = builder.Build();
app.UseDevHostGuard(hosts, options.LanAddress is not null, loopbackOrigin, options.TunnelOrigin);
app.UseRefreshFaults();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
// The JavaScript token client, straight from clients/javascript/src, for the /dev pages. A host gets it from the
// ShiftIdentity.Blazor package's static web assets instead.
var clients = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "clients", "javascript", "src"));
if (Directory.Exists(clients))
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(clients), RequestPath = "/clients" });
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
ConfiguredIdentityHttpHost<IdentityTestDbContext>.MapHostEndpoints(app);
app.MapDevEndpoints();
// An unknown API or tool route must never fall through to the browser client's page.
app.Map("/api/{**path}", () => Results.NotFound());
app.Map("/dev/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

await app.StartAsync();
var bound = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
Console.WriteLine();
Console.WriteLine($"DEVHOST_URL={loopbackOrigin}/");
if (lanOrigin is not null) Console.WriteLine($"DEVHOST_LAN_URL={lanOrigin}/");
if (options.TunnelOrigin is not null) Console.WriteLine($"DEVHOST_TUNNEL_URL={options.TunnelOrigin}/");
Console.WriteLine($"  Listening on:      {string.Join(", ", bound)}");
Console.WriteLine($"  Sign in:           {publicOrigin}/Identity/login");
Console.WriteLine($"  DevHost tools:     {loopbackOrigin}/dev/");
Console.WriteLine($"  Device simulator:  {loopbackOrigin}/dev/device.html");
Console.WriteLine($"  Token client lab:  {loopbackOrigin}/dev/token-client.html");
Console.WriteLine($"  Accounts (password \"{fixture.Password}\"):");
foreach (var account in accounts) Console.WriteLine($"    {account.Username,-16} {account.Description}");
if (options.TunnelOrigin is not null)
{
    Console.WriteLine($"  Through the tunnel, phones reach the Identity app only; the tools above stay on this machine.");
    Console.WriteLine("  Keep the tunnel's access Private or Organization: the accounts' password is in the public repo.");
}
if (abandoned > 0) Console.WriteLine($"  Dropped {abandoned} database(s) left by DevHost runs that ended without stopping.");
Console.WriteLine("  Owned synthetic SQL database only. Press Ctrl+C to stop; stopping removes the database.");
await app.WaitForShutdownAsync();
