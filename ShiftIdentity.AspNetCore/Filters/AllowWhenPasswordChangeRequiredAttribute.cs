namespace ShiftSoftware.ShiftIdentity.AspNetCore.Filters;

/// <summary>
/// Marks an endpoint as reachable while the caller holds a challenge token (i.e. their token
/// carries <see cref="Core.ShiftIdentityClaims.RequirePasswordChange"/> = true). Without this
/// marker, <see cref="RequirePasswordChangeFilter"/> rejects the request with HTTP 403 so a
/// challenged user cannot exercise the rest of the API before completing the forced change.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AllowWhenPasswordChangeRequiredAttribute : Attribute
{
}
