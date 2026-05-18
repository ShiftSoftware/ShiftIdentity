using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Filters;

/// <summary>
/// Rejects MVC requests whose principal carries <see cref="ShiftIdentityClaims.RequirePasswordChange"/>
/// unless the target endpoint is annotated with <see cref="AllowWhenPasswordChangeRequiredAttribute"/>
/// or <c>[AllowAnonymous]</c>. This is what makes a challenge token unable to exercise the
/// rest of the API — the only thing it can reach is <c>POST /Auth/CompletePasswordChange</c>.
/// </summary>
public sealed class RequirePasswordChangeFilter : IAuthorizationFilter
{
    /// <summary>
    /// Response header set on rejected requests. Clients can detect this and route the user
    /// to the change-password page instead of treating the 403 as a generic permission error.
    /// </summary>
    public const string MarkerHeader = "X-Require-Password-Change";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
            return;

        var claim = user.FindFirst(ShiftIdentityClaims.RequirePasswordChange)?.Value;
        if (!bool.TryParse(claim, out var requireChange) || !requireChange)
            return;

        var endpoint = context.HttpContext.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() != null)
            return;
        if (endpoint?.Metadata.GetMetadata<AllowWhenPasswordChangeRequiredAttribute>() != null)
            return;

        context.HttpContext.Response.Headers[MarkerHeader] = "true";
        context.Result = new ObjectResult(new { error = "password_change_required" })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }
}
