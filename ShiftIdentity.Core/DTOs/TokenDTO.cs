namespace ShiftSoftware.ShiftIdentity.Core.DTOs;


public class TokenDTO
{
    public string Token { get; set; } = default!;
    public long? TokenLifeTimeInSeconds { get; set; }

    public string RefreshToken { get; set; } = default!;
    public long? RefreshTokenLifeTimeInSeconds { get; set; }

    public bool RequirePasswordChange { get; set; }

    public TokenUserDataDTO UserData { get; set; } = default!;
}