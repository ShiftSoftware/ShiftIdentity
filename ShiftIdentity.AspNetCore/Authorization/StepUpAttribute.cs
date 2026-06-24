using Microsoft.AspNetCore.Authorization;
using ShiftSoftware.ShiftIdentity.Core.Authorization;
using ShiftSoftware.ShiftIdentity.Core.Enums;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authorization;

/// <summary>
/// Marks an endpoint as a step-up operation for a given <see cref="AuthPurpose"/> purpose.
/// By default the endpoint accepts either a full internal access token (logged-in user, voluntary) or a
/// temporary token bound to the same purpose (enforced pre-login flow) — see <see cref="StepUpAuthorizationHandler"/>.
/// <para>
/// Set <c>allowAccessToken: false</c> for pre-login completion endpoints (e.g. the 2FA login step) that
/// must accept <em>only</em> the matching purpose-bound temporary token and never an already-authenticated
/// access token.
/// </para>
/// <para>
/// Adding a new flow is just: add an <see cref="AuthPurpose"/> value, issue a temporary token for it,
/// and annotate the endpoint with <c>[StepUp(AuthPurpose.NewFlow)]</c>. The endpoint body never deals
/// with credentials — it reads the user id from <c>User</c>.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class StepUpAttribute : AuthorizeAttribute
{
    public StepUpAttribute(AuthPurpose purpose, bool allowAccessToken = true)
    {
        Policy = StepUpPolicy.For(purpose, allowAccessToken);
    }
}
