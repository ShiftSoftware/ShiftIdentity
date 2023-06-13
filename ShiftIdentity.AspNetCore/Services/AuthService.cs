﻿using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Core.Repositories;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

public class AuthService
{
    private readonly IUserRepository userRepo;
    private readonly ShiftIdentityOptions shiftIdentityOptions;
    private readonly TokenService tokenService;
    private readonly AuthCodeService authCodeService;

    public AuthService(
        IUserRepository userRepo,
        ShiftIdentityOptions configuration,
        TokenService tokenService,
        AuthCodeService authCodeService
        )
    {
        this.userRepo = userRepo;
        this.shiftIdentityOptions = configuration;
        this.tokenService = tokenService;
        this.authCodeService = authCodeService;
    }

    public async Task<LoginResultModel> LoginAsync(LoginDTO loginDto)
    {
        var user = await userRepo.GetUserByUsernameAsync(loginDto.Username);

        if (user is null)
            return new LoginResultModel(LoginResultEnum.UsernameIncorrect, "Username is incorrect");

        if (!HashService.VerifyPassword(loginDto.Password, user.Salt, user.PasswordHash))
        {
            //If password is incorrect update logigattempts
            user.LoginAttempts++;

            if (user.LoginAttempts >= shiftIdentityOptions.Configuration.Security.LoginAttemptsForLockDown)
            {
                //If user exceeds login attemps for lockdown
                //Reset loginattemps and set lockdownuntil
                user.LoginAttempts = 0;
                user.LockDownUntil = DateTime.UtcNow.AddMinutes(shiftIdentityOptions.Configuration.Security.LockDownInMinutes);
            }

            await userRepo.SaveChangesAsync();

            return new LoginResultModel(LoginResultEnum.PasswordIncorrect, "Password is incorrect");
        }

        //If user deactive
        if (!user.IsActive)
            return new LoginResultModel(LoginResultEnum.UserDeactive, "The user is deactivated");

        //If user is lockdown
        var lockdownUntil = user.LockDownUntil ?? new DateTime(0);
        if (lockdownUntil > DateTime.UtcNow)
            return new LoginResultModel(LoginResultEnum.UserLockDown, $"User is lockdown for {shiftIdentityOptions.Configuration.Security.LockDownInMinutes} minutes");

        //If user credentials are correct, then reset loginattempt and lockdownuntil
        user.LoginAttempts = 0;
        user.LockDownUntil = null;

        await userRepo.SaveChangesAsync();

        var token = await tokenService.GenerateInternalJwtTokenAsync(user);

        return new LoginResultModel(token);
    }

    public async Task<TokenDTO?> GenrerateExternalTokenWithAppIdOnly(GenerateExternalTokenWithAppIdOnlyDTO dto)
    {
        var authCode = await authCodeService.VerifyCodeByAppIdOnly(dto.AppId, dto.AuthCode, dto.CodeVerifier);

        if (authCode is null)
            return null;

        var user = await userRepo.FindAsync(authCode.UserID);

        if (user is null)
            return null;

        var token = tokenService.GenerateExternalJwtToken(user, authCode);

        return token;
    }
}
