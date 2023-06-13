namespace ShiftSoftware.ShiftIdentity.Core.DTOs
{

    public class TokenDTO
    {
        public string Token { get; set; }

        public string RefreshToken { get; set; }

        public bool RequirePasswordChange { get; set; }

        public TokenUserDataDTO UserData { get; set; }
    }
}