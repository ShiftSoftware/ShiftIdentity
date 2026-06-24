using Microsoft.AspNetCore.Authorization;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Enums;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authorization;

/// <summary>
/// Grants a <see cref="StepUpRequirement"/> when the caller is either:
/// <list type="number">
///   <item>authenticated with a full <em>internal</em> access token (logged in, acting voluntarily), or</item>
///   <item>authenticated with a temporary token whose <c>TokenPurpose</c> matches the endpoint's purpose
///         (an enforced pre-login flow such as forced change-password / two-factor enrollment).</item>
/// </list>
/// External (third-party app) access tokens are rejected for self-service security operations,
/// preserving the behavior of the former <c>"ChangePassword"</c> policy.
/// </summary>
public class StepUpAuthorizationHandler : AuthorizationHandler<StepUpRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, StepUpRequirement requirement)
    {
        var user = context.User;

        if (user?.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        var purposeClaim = user.FindFirst(ShiftIdentityClaims.TokenPurpose)?.Value;

        if (string.IsNullOrEmpty(purposeClaim))
        {
            // No purpose claim → a full access token. Pre-login completion endpoints opt out of this
            // (AllowAccessToken == false): an already-authenticated session must not satisfy them.
            if (!requirement.AllowAccessToken)
                return Task.CompletedTask;

            // Otherwise only first-party (internal) tokens may run self-service security operations;
            // block external/third-party app tokens.
            var external = user.FindFirst(ShiftIdentityClaims.ExternalToken)?.Value;
            if (string.Equals(external, "false", StringComparison.OrdinalIgnoreCase))
                context.Succeed(requirement);
        }
        else if (Enum.TryParse<AuthPurpose>(purposeClaim, out var purpose)
                 && purpose != AuthPurpose.None
                 && purpose == requirement.Purpose)
        {
            // A temporary token bound to exactly this purpose.
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
