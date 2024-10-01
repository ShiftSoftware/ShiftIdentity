using PhoneNumbers;
using ShiftSoftware.ShiftIdentity.AspNetCore.Models;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.IRepositories;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.Security.Claims;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

public class AuthService
{
    private readonly IUserRepository userRepo;
    private readonly ShiftIdentityConfiguration shiftIdentityConfigurations;
    private readonly TokenService tokenService;
    private readonly AuthCodeService authCodeService;
    private readonly ShiftIdentityLocalizer Loc;

    public AuthService(
        IUserRepository userRepo,
        ShiftIdentityConfiguration shiftIdentityConfiguration,
        TokenService tokenService,
        AuthCodeService authCodeService,
        ShiftIdentityLocalizer Loc
        )
    {
        this.userRepo = userRepo;
        this.shiftIdentityConfigurations = shiftIdentityConfiguration;
        this.tokenService = tokenService;
        this.authCodeService = authCodeService;
        this.Loc = Loc;
    }

    public async Task<LoginResultModel> LoginAsync(LoginDTO loginDto)
    {
        var user = await userRepo.GetUserByUsernameAsync(loginDto.Username);

        if (user is null)
            return new LoginResultModel(LoginResultEnum.UsernameIncorrect, Loc["Username or password is incorrect"]);

        if (!HashService.VerifyPassword(loginDto.Password, user.Salt, user.PasswordHash))
        {
            //If password is incorrect update logigattempts
            user.LoginAttempts++;

            if (user.LoginAttempts >= shiftIdentityConfigurations.Security.LoginAttemptsForLockDown)
            {
                //If user exceeds login attemps for lockdown
                //Reset loginattemps and set lockdownuntil
                user.LoginAttempts = 0;
                user.LockDownUntil = DateTime.UtcNow.AddMinutes(shiftIdentityConfigurations.Security.LockDownInMinutes);
            }

            await userRepo.SaveChangesAsync();

            return new LoginResultModel(LoginResultEnum.PasswordIncorrect, Loc["Username or password is incorrect"]);
        }

        //If user deactive
        if (!user.IsActive)
            return new LoginResultModel(LoginResultEnum.UserDeactive, Loc["The user is deactivated"]);

        //If user is lockdown
        var lockdownUntil = user.LockDownUntil ?? new DateTime(0);
        if (lockdownUntil > DateTime.UtcNow)
            return new LoginResultModel(LoginResultEnum.UserLockDown, Loc["User is lockdown for {0} minutes", shiftIdentityConfigurations.Security.LockDownInMinutes]);

        //If user credentials are correct, then reset loginattempt and lockdownuntil
        user.LoginAttempts = 0;
        user.LockDownUntil = null;

        //Update lastseen
        if(user.UserLog is null)
            user.UserLog = new Core.Entities.UserLog { LastSeen = DateTimeOffset.UtcNow };
        else
            user.UserLog.LastSeen = DateTimeOffset.UtcNow;

        await userRepo.SaveChangesAsync();

        var token = tokenService.GenerateInternalJwtToken(user);

        return new LoginResultModel(token);
    }

    public async Task<TokenDTO?> GenrerateExternalTokenWithAppIdOnly(GenerateExternalTokenWithAppIdOnlyDTO dto)
    {
        var authCode = await authCodeService.VerifyCodeByAppIdOnly(dto.AppId, dto.AuthCode, dto.CodeVerifier);

        if (authCode is null)
        {
            Console.WriteLine("VerifyCodeByAppIdOnly returnd null");
            return null;
        }

        var user = await userRepo.FindAsync(authCode.UserID);

        if (user is null)
        {
            Console.WriteLine("userRepo returnd null");
            return null;
        }

        var token = tokenService.GenerateExternalJwtToken(user, authCode);

        return token;
    }

    public async Task<TokenDTO?> RefreshAsync(string refreshToken)
    {
        try
        {
            var claimPrincipal = tokenService.GetPrincipalFromRefreshToken(refreshToken);

            if (claimPrincipal is null)
                return null;

            var userId = long.Parse(claimPrincipal?.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await userRepo.FindAsync(userId);

            if(user is null || !user.IsActive || user.IsDeleted)
                return null;

            var token = tokenService.GenerateToken(user);

            //Update lastseen
            if (user.UserLog is null)
                user.UserLog = new Core.Entities.UserLog { LastSeen = DateTimeOffset.UtcNow };
            else
                user.UserLog.LastSeen = DateTimeOffset.UtcNow;

            await this.userRepo.SaveChangesAsync();

            return token;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
