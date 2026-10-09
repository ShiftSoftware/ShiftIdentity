using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core.Authentication;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Endpoints;

/// <summary>
/// Device sign-in routes under api/identity/v2 (RFC 8628). None of them takes a session. The device calls
/// <c>device/authorize</c> and <c>device/token</c>: its secret device code is its proof. The phone calls the lookup and
/// deny routes with the public user code, and approve with the username and password of the account the device should
/// use. The group's filter adds <c>no-store</c> to every answer.
/// </summary>
internal static class DeviceAuthorizationEndpoints
{
    internal static void MapDeviceAuthorizationEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/device/authorize", async (StartDeviceAuthorizationRequest request, HttpContext context,
            IdentityAdmissionServices services, CancellationToken ct) =>
            AdmissionEndpoints.Result(await AuthService.StartDeviceAuthorizationAsync(services, request, Address(context), ct)));
        group.MapPost("/device/token", async (DeviceTokenRequest request, HttpContext context,
            IdentityAdmissionServices services, CancellationToken ct) =>
            TokenResult(await AuthService.PollDeviceAuthorizationAsync(services, request, Address(context), ct)));
        group.MapGet("/device/{userCode}", async (string userCode, HttpContext context, IdentityAdmissionServices services, CancellationToken ct) =>
            AdmissionEndpoints.Result(await AuthService.ReadDeviceAuthorizationAsync(services, userCode, Address(context), ct)));
        group.MapPost("/device/approve", async (ApproveDeviceRequest request, HttpContext context,
            IdentityAdmissionServices services, CancellationToken ct) =>
            AdmissionEndpoints.Result(await AuthService.ApproveDeviceAuthorizationAsync(services, request, Address(context), ct)));
        group.MapPost("/device/deny", async (DeviceUserCodeRequest request, HttpContext context,
            IdentityAdmissionServices services, CancellationToken ct) =>
            AdmissionEndpoints.Result(await AuthService.DenyDeviceAuthorizationAsync(services, request, Address(context), ct)));
    }

    private static string Address(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // As RFC 8628 answers a poll: every refusal is a 400 with its error code, except a temporary failure, which is a 503.
    private static IResult TokenResult(AuthOutcome result) => Results.Json<AuthOutcome>(result, statusCode: result switch
    {
        AuthenticationRefused { Code: AuthenticationFailure.Unavailable } => 503,
        AuthenticationRefused => 400,
        _ => 200
    });
}
