using PhoneNumbers;
using ShiftSoftware.ShiftIdentity.AspNetCore.Models;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;
using ShiftSoftware.ShiftIdentity.Core.IRepositories;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.Security.Claims;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

public class AuthService
{
    private readonly IUserRepository userRepo;
    private readonly ShiftIdentityConfiguration shiftIdentityConfigurations;
    private readonly TokenService tokenService;
    private readonly ShiftIdentityLocalizer Loc;

    public AuthService(
        IUserRepository userRepo,
        ShiftIdentityConfiguration shiftIdentityConfiguration,
        TokenService tokenService,
        ShiftIdentityLocalizer Loc
        )
    {
        this.userRepo = userRepo;
        this.shiftIdentityConfigurations = shiftIdentityConfiguration;
        this.tokenService = tokenService;
        this.Loc = Loc;
    }

    public async Task<LoginResultModel> LoginAsync(LoginDTO loginDto)
    {
        var user = await userRepo.GetUserByUsernameAsync(loginDto.Username);

        if (user is null)
            return new LoginResultModel(LoginResultEnum.UsernameIncorrect, Loc["Username or password is incorrect"]);

        if (!HashService.VerifyPassword(loginDto.Password, user.Salt, user.PasswordHash))
        {
            //If password is incorrect update LoginAttempts
            user.LoginAttempts++;

            if (user.LoginAttempts >= shiftIdentityConfigurations.Security.LoginAttemptsForLockDown)
            {
                //If user exceeds login attempts for lockdown
                //Reset LoginAttempts and set LockDownUntil
                user.LoginAttempts = 0;
                user.LockDownUntil = DateTime.UtcNow.AddMinutes(shiftIdentityConfigurations.Security.LockDownInMinutes);
            }

            await userRepo.SaveChangesAsync();

            return new LoginResultModel(LoginResultEnum.PasswordIncorrect, Loc["Username or password is incorrect"]);
        }

        //If user is not active
        if (!user.IsActive)
            return new LoginResultModel(LoginResultEnum.UserDeactive, Loc["The user is deactivated"]);

        //If user is lockdown
        var lockdownUntil = user.LockDownUntil ?? new DateTime(0);
        if (lockdownUntil > DateTime.UtcNow)
            return new LoginResultModel(LoginResultEnum.UserLockDown, Loc["User is lockdown for {0} minutes", shiftIdentityConfigurations.Security.LockDownInMinutes]);

        //If user credentials are correct, then reset LoginAttempts and LockDownUntil
        user.LoginAttempts = 0;
        user.LockDownUntil = null;

        //Update LastSeen
        if (user.UserLog is null)
            user.UserLog = new Core.Entities.UserLog { LastSeen = DateTimeOffset.UtcNow };
        else
            user.UserLog.LastSeen = DateTimeOffset.UtcNow;

        await userRepo.SaveChangesAsync();

        // When a password change is required, hand out a short-lived challenge token: it carries
        // the RequirePasswordChange claim and, via RequirePasswordChangeFilter, only unlocks
        // /Auth/CompletePasswordChange. No refresh token, so the user must complete the change.
        var mustChange = shiftIdentityConfigurations.Security.RequirePasswordChange && user.RequireChangePassword;
        var token = mustChange
            ? tokenService.GenerateChallengeToken(user)
            : tokenService.GenerateInternalJwtToken(user);

        return new LoginResultModel(token);
    }

    /// <summary>
    /// Completes the forced password-change flow: validates the caller is in challenge state,
    /// sets the new password, clears the flag, and returns a full session token. The user is
    /// considered "logged in" only after this call succeeds.
    /// </summary>
    public async Task<TokenDTO?> CompletePasswordChangeAsync(long userId, CompletePasswordChangeDTO dto)
    {
        var user = await userRepo.FindAsync(userId, asOf: null, disableDefaultDataLevelAccess: true, disableGlobalFilters: true);
        if (user is null || !user.IsActive || user.IsDeleted)
            return null;

        var hash = HashService.GenerateHash(dto.NewPassword);
        user.PasswordHash = hash.PasswordHash;
        user.Salt = hash.Salt;
        user.RequireChangePassword = false;

        // Invalidate any refresh tokens issued before this change. The token minted below carries
        // the new stamp, so this session survives; every other outstanding session is killed.
        user.RegenerateSecurityStamp();

        if (user.UserLog is null)
            user.UserLog = new Core.Entities.UserLog { LastSeen = DateTimeOffset.UtcNow };
        else
            user.UserLog.LastSeen = DateTimeOffset.UtcNow;

        await userRepo.SaveChangesAsync();

        return tokenService.GenerateInternalJwtToken(user);
    }

    public async Task<TokenDTO?> RefreshAsync(string refreshToken)
    {
        try
        {
            var claimPrincipal = tokenService.GetPrincipalFromRefreshToken(refreshToken);

            if (claimPrincipal is null)
                return null;

            var userId = long.Parse(claimPrincipal?.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await userRepo.FindAsync(userId, disableDefaultDataLevelAccess: true, disableGlobalFilters: true);

            if(user is null || !user.IsActive || user.IsDeleted)
                return null;

            // Reject refresh tokens minted before the user's security stamp last rolled (password
            // change / log-out-everywhere). A missing or unparseable claim means a pre-stamp token,
            // which is also rejected — those sessions must re-authenticate once after deploy.
            var stampClaim = claimPrincipal!.FindFirstValue(ShiftIdentityClaims.SecurityStamp);
            if (!Guid.TryParse(stampClaim, out var tokenStamp) || tokenStamp != user.SecurityStamp)
                return null;

            var token = tokenService.GenerateToken(user);

            //Update LastSeen
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
