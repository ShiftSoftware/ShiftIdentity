using ShiftSoftware.ShiftIdentity.Core.Enums;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs;


public class TokenDTO
{
    public string Token { get; set; } = default!;
    public long? TokenLifeTimeInSeconds { get; set; }

    public string RefreshToken { get; set; } = default!;
    public long? RefreshTokenLifeTimeInSeconds { get; set; }

    public AuthPurpose Flow { get; set; } = AuthPurpose.None;

    public TokenUserDataDTO UserData { get; set; } = default!;
}