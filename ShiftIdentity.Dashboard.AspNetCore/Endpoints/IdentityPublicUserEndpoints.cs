using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.Routing;
using Microsoft.OData.ModelBuilder;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Data.IRepositories;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Endpoints;

// The public-user OData list, ported verbatim from IdentityPublicUserController (route + verb byte-identical:
// GET api/IdentityPublicUser). The controller's [Authorize] (no TypeAuth) becomes .RequireAuthorization()
// (authenticated only). Composed into the dashboard by MapShiftIdentityDashboard().
internal static class IdentityPublicUserEndpoints
{
    public static IEndpointRouteBuilder MapIdentityPublicUserEndpoints(this IEndpointRouteBuilder app)
    {
        // GET api/IdentityPublicUser — authenticated; returns an OData-shaped public projection (id + name only).
        app.MapGet("api/IdentityPublicUser",
            async (HttpContext httpContext, IUserRepository userRepository) =>
            {
                var oDataQueryOptions = BuildODataQueryOptions<PublicUserListDTO>(httpContext.Request);

                var data = (await userRepository.OdataList())
                    .Select(x => new PublicUserListDTO { ID = x.ID, Name = x.FullName });

                return Results.Ok(await data.ToOdataDTO(oDataQueryOptions, httpContext.Request, applySoftDeleteFilter: false));
            })
            .RequireAuthorization();

        return app;
    }

    private static readonly ConcurrentDictionary<Type, ODataQueryContext> _odataContextCache = new();

    // Minimal API has no first-class binder for ODataQueryOptions<T>, so build it from an EDM model (per-type
    // context cached). Mirrors ShiftEntity.Web's internal BuildODataQueryOptions that its attribute-driven CRUD
    // list endpoints use — replicated here because that helper is internal to ShiftEntity.Web.
    private static ODataQueryOptions<T> BuildODataQueryOptions<T>(HttpRequest request) where T : class
    {
        var context = _odataContextCache.GetOrAdd(typeof(T), t =>
        {
            var builder = new ODataConventionModelBuilder();
            builder.EntitySet<T>($"{typeof(T).Name}Set");
            var model = builder.GetEdmModel();
            return new ODataQueryContext(model, typeof(T), new());
        });

        return new ODataQueryOptions<T>(context, request);
    }
}
