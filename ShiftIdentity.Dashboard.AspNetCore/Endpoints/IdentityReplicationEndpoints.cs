using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShiftSoftware.ShiftEntity.CosmosDbReplication.Services;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Replication;
using ShiftSoftware.ShiftIdentity.Data.Replication;
using ShiftSoftware.TypeAuth.AspNetCore.EndpointFilters;
using ShiftSoftware.TypeAuth.Core;
using ShiftSoftware.TypeAuth.Core.Actions;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Endpoints;

// The Identity catch-up, run from the API with the caller's own token (see AddShiftIdentityReplicationEndpoints).
// Mapped by MapShiftIdentityDashboard() only when the host registered them.
//
//   POST api/IdentityReplication/{entity}   one entity; needs Write on its action
//   POST api/IdentityReplication/all        every entity the caller may write; the others are listed as skipped
//
// Each rebuilds every row of its entities (?onlyDirty=true: only the rows not yet replicated), and answers 200 with
// the report, or 400 with the report when a step or a row failed. Failed rows stay dirty for the next run.
internal static class IdentityReplicationEndpoints
{
    // Who may replicate each entity: whoever may write it. Which users hold a tree is edited on the user, and who is
    // in a team on the team, so those follow Users and Teams.
    private static readonly (IdentityReplicationEntity Entity, string Route, ReadWriteDeleteAction Action)[] Entities =
    [
        (IdentityReplicationEntity.Service, "services", ShiftIdentityActions.Services),
        (IdentityReplicationEntity.Department, "departments", ShiftIdentityActions.Departments),
        (IdentityReplicationEntity.Brand, "brands", ShiftIdentityActions.Brands),
        (IdentityReplicationEntity.Region, "regions", ShiftIdentityActions.Regions),
        (IdentityReplicationEntity.Country, "countries", ShiftIdentityActions.Countries),
        (IdentityReplicationEntity.City, "cities", ShiftIdentityActions.Cities),
        (IdentityReplicationEntity.CompanyBranch, "branches", ShiftIdentityActions.CompanyBranches),
        (IdentityReplicationEntity.Company, "companies", ShiftIdentityActions.Companies),
        (IdentityReplicationEntity.Team, "teams", ShiftIdentityActions.Teams),
        (IdentityReplicationEntity.User, "users", ShiftIdentityActions.Users),
        (IdentityReplicationEntity.AccessTree, "access-trees", ShiftIdentityActions.AccessTrees),
        (IdentityReplicationEntity.UserAccessTree, "user-access-trees", ShiftIdentityActions.Users),
        (IdentityReplicationEntity.TeamUser, "team-users", ShiftIdentityActions.Teams),
    ];

    public static IEndpointRouteBuilder MapIdentityReplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var registration = app.ServiceProvider.GetService<IdentityReplicationEndpointsRegistration>();
        if (registration is null)
            return app;

        var group = app.MapGroup("api/IdentityReplication").RequireAuthorization();

        foreach (var (entity, route, action) in Entities.Where(x => registration.Entities.Contains(x.Entity)))
        {
            group.MapPost(route, (HttpContext context, bool? onlyDirty) =>
                    RunAsync(context, registration, [entity], [], onlyDirty))
                .RequireTypeAuthWrite(action);
        }

        group.MapPost("all", (HttpContext context, ITypeAuthService typeAuth, bool? onlyDirty) =>
        {
            var allowed = Entities.Where(x => registration.Entities.Contains(x.Entity) && typeAuth.CanWrite(x.Action)).Select(x => x.Entity).ToList();
            if (allowed.Count == 0)
                return Task.FromResult(Results.Forbid());

            var skipped = registration.Entities.Except(allowed).Select(x => x.ToString()).ToList();
            return RunAsync(context, registration, allowed, skipped, onlyDirty);
        });

        return app;
    }

    private static async Task<IResult> RunAsync(HttpContext context, IdentityReplicationEndpointsRegistration registration,
        List<IdentityReplicationEntity> entities, List<string> skipped, bool? onlyDirty)
    {
        var services = context.RequestServices;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IdentityReplicationEndpoints).FullName!);
        var cosmos = services.GetRequiredService<CosmosDBReplication>();

        IdentityCatchUpReport report;
        try
        {
            report = await registration.Run(cosmos, registration.Client(services), entities, onlyDirty != true);
        }
        catch (IdentityCatchUpException ex)
        {
            report = ex.Report;
        }

        var body = new IdentityReplicationResponse(
            report.Succeeded,
            report.ToString(),
            [.. report.Steps.Select(x => new IdentityReplicationStepResponse(x.Entity, x.Succeeded, x.ToString()))],
            skipped);

        if (report.Succeeded)
        {
            logger.LogInformation("Identity replication by {User}: {Report}", context.User.Identity?.Name, body.Report);
            return Results.Ok(body);
        }

        logger.LogError("Identity replication by {User} failed: {Report}", context.User.Identity?.Name, body.Report);
        return Results.BadRequest(body);
    }
}

/// <summary>What an <c>api/IdentityReplication</c> call did: one entry per entity it ran, and the ones it skipped.</summary>
public sealed record IdentityReplicationResponse(bool Succeeded, string Report, IReadOnlyList<IdentityReplicationStepResponse> Steps, IReadOnlyList<string> Skipped);

/// <summary>One entity of an <c>api/IdentityReplication</c> call.</summary>
public sealed record IdentityReplicationStepResponse(string Entity, bool Succeeded, string Result);
