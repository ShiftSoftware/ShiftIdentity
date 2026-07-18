using Microsoft.AspNetCore.Routing;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Endpoints;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions;

// The app-side companion to the DI-side AddShiftIdentityDashboard(...): wires up the ShiftIdentity dashboard's
// CUSTOM (non-CRUD) endpoints. Attribute-driven CRUD is discovered + mapped by the host's
// app.MapShiftEntityEndpoints<DB>(); this is only the hand-written endpoints that used to live on the classic
// controllers. The host calls MapShiftIdentityDashboard() once (inside its internal-hosting block).
//
// This is a thin AGGREGATOR: each feature keeps its custom endpoints in its own *Endpoints class under Endpoints/
// (e.g. CompanyCalendarEndpoints). Add a feature by dropping a class there and calling its Map… method below —
// Phases 4-5 (User, standalone controllers → minimal APIs) extend it here without another host change.
public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapShiftIdentityDashboard(this IEndpointRouteBuilder app)
    {
        app.MapCompanyCalendarEndpoints();
        // Phases 4-5: app.MapUserEndpoints(); app.MapUserManagerEndpoints(); app.MapAuthEndpoints(); …
        return app;
    }
}
