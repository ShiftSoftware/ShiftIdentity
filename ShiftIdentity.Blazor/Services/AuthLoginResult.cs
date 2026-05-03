using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

/// <summary>
/// Strategy-agnostic login result passed from the login UI to <see cref="AuthSessionService.OnLoginSuccessAsync"/>.
/// JWT path populates <see cref="Token"/>; cookie path populates <see cref="Claims"/>. Each strategy reads
/// the field it needs and ignores the other.
/// </summary>
public class AuthLoginResult
{
    public TokenDTO? Token { get; init; }

    public List<UserClaimModel>? Claims { get; init; }
}
