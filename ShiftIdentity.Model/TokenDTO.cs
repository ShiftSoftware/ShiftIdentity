namespace ShiftSoftware.ShiftIdentity.Model;

public class TokenDTO
{
    public string Token { get; set; } = default!;

    public string RefreshToken { get; set; } = default!;

    public bool RequirePasswordChange { get; set; }

    public TokenUserDataDTO? UserData { get; set; }
}
