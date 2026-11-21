using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Seeding;
using ShiftSoftware.TypeAuth.Core;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions;

public static class WebApplicationExtensions
{
    public static async Task<WebApplication> SeedDBAsync(this WebApplication app, string adminUserName, string adminPassword, DBSeedOptions? dBSeedOptions = null)
    {
        using var scope = app.Services.CreateScope();

        var scopedServices = scope.ServiceProvider;

        var db = scopedServices.GetRequiredService<ShiftIdentityDbContext>();

        var actionTrees = GetRegisteredActionTrees(scopedServices);

        await new DBSeed(db, actionTrees, adminUserName, adminPassword, dBSeedOptions)
        {
            // With the identity authority registered, the built-in user gets its security row with the user.
            CreateSecurityState = scopedServices.GetService<ShiftSoftware.ShiftIdentity.Data.Services.IUserAccountAuthority>() is not null
        }.SeedAsync();

        return app;
    }

    /// <summary>
    /// <b>SQLite only.</b> Seeds the identity tables of the host's <see cref="ShiftIdentityDbContext"/>, in one
    /// transaction. Call it after the schema is created (<c>EnsureCreated</c>) and before <c>app.Run()</c>, inside
    /// your own <c>if</c> for the environments that should be seeded. On any other database it seeds nothing, logs a
    /// warning and returns <see cref="IdentitySeedResult.Skipped"/>; SQL Server hosts use <see cref="SeedDBAsync"/>.
    /// </summary>
    /// <exception cref="IdentitySeedException">The seed names something that is not there; nothing was written.</exception>
    public static async Task<IdentitySeedResult> SeedShiftIdentitySqliteAsync(this IHost host, IdentitySeedData seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("ShiftSoftware.ShiftIdentity.Seeding");
        return await new IdentitySeeder(services.GetRequiredService<ShiftIdentityDbContext>(), GetRegisteredActionTrees(services), logger)
            .SeedAsync(seed, cancellationToken);
    }

    /// <summary>
    /// <b>SQLite only.</b> The same as the <see cref="IdentitySeedData"/> overload, from JSON of exactly that shape
    /// (names in any case, enums by name, an unknown property is an error).
    /// </summary>
    /// <exception cref="IdentitySeedException">The JSON does not parse, or the seed names something that is not there; nothing was written.</exception>
    public static Task<IdentitySeedResult> SeedShiftIdentitySqliteAsync(this IHost host, string json,
        CancellationToken cancellationToken = default) =>
        host.SeedShiftIdentitySqliteAsync(IdentitySeedJson.Read(json), cancellationToken);

    public static async Task<WebApplication> SetFullAccessAsync(this WebApplication app, params string[] username)
    {
        using var scope = app.Services.CreateScope();
        var scopedServices = scope.ServiceProvider;

        var db = scopedServices.GetRequiredService<ShiftIdentityDbContext>();

        var users = await db.Users.Where(x => username.Contains(x.Username)).ToListAsync();

        var jsonTree = FullAccessTree.BuildJson(GetRegisteredActionTrees(scopedServices));

        // Grant full access only. These are real admin users, not seed data, so they are intentionally
        // NOT marked IsProtected — that flag blocks editing, deleting, and password resets (see UserRepository).
        foreach (var user in users)
        {
            user.AccessTree = jsonTree;
        }

        await db.SaveChangesAsync();

        return app;
    }

    // The registered action trees (from TypeAuth), always including ShiftIdentity's own actions — the set
    // a built-in admin is granted full access over.
    private static List<Type> GetRegisteredActionTrees(IServiceProvider services)
    {
        var actionTrees = new List<Type>();

        var typeAuthService = services.GetService<ITypeAuthService>();

        if (typeAuthService is not null)
            actionTrees.AddRange(typeAuthService.GetRegisteredActionTrees());

        if (!actionTrees.Contains(typeof(ShiftIdentityActions)))
            actionTrees.Add(typeof(ShiftIdentityActions));

        return actionTrees;
    }
}
