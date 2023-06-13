using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Core.Models;

public class LoginResultModel
{
    public LoginResultEnum Result { get; set; }

    public string? ErrorMessage { get; set; }

    public TokenDTO Token { get; set; } = default!;

    public LoginResultModel()
    {

    }

    public LoginResultModel(LoginResultEnum result, string? errorMessage = null)
    {
        Result = result;
        ErrorMessage = errorMessage;
    }

    public LoginResultModel(TokenDTO token)
    {
        Result = LoginResultEnum.Success;
        Token = token;
    }
}

public enum LoginResultEnum
{
    Success = 1,
    UsernameIncorrect = 2,
    PasswordIncorrect = 3,
    UserLockDown = 4,
    UserDeactive = 5,
}
