using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Endpoints;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Extentsions;

// The app-side companion to the DI-side AddShiftIdentityDashboard(...): wires up ALL of the ShiftIdentity server's
// hand-written (non-CRUD) endpoints. Attribute-driven CRUD is discovered + mapped by the host's
// app.MapShiftEntityEndpoints<DB>(); this is only the endpoints that used to live on the classic controllers. The
// host calls MapShiftIdentityDashboard() once (inside its internal-hosting block) — a single entry point for the
// whole identity server, since Auth is part of it (login/refresh/MFA/auth-code) rather than a separate concern.
//
// This is a thin AGGREGATOR: each feature keeps its endpoints in its own *Endpoints class (CompanyCalendarEndpoints,
// UserEndpoints under Endpoints/; ShiftIdentityAuthEndpoints in the core ShiftIdentity.AspNetCore assembly). Add a
// feature by dropping a class in and calling its Map… method below — no further host change.
public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapShiftIdentityDashboard(this IEndpointRouteBuilder app)
    {
        // Auth (login/refresh/MFA/auth-code/external-token) — lives in the core ShiftIdentity.AspNetCore assembly
        // (was the API AuthController). Folded in here so the host wires the entire identity server with one call.
        app.MapShiftIdentityAuthEndpoints();

        app.MapCompanyCalendarEndpoints();
        app.MapUserEndpoints();
        app.MapReverseTypeAuthLookupEndpoints();
        app.MapIdentityPublicUserEndpoints();
        app.MapUserManagerEndpoints();
        app.MapIdentitySyncEndpoints();
        return app;
    }
}
