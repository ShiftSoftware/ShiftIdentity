﻿using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Core.Services;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.AspNetCore;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;
using ShiftSoftware.TypeAuth.AspNetCore;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers;

[Route("api/[controller]")]
public class IdentityUserController : ShiftEntitySecureControllerAsync<UserRepository, User, UserListDTO, UserDTO>
{
    private readonly UserRepository userRepo;
    private readonly IMapper mapper;
    private readonly ShiftIdentityConfiguration options;
    private readonly IEnumerable<ISendEmailVerification>? sendEmailVerifications;
    private readonly IEnumerable<ISendUserInfo>? sendUserInfos;

    public IdentityUserController(UserRepository userRepo,
        IMapper mapper,
        ShiftIdentityConfiguration options,
        IEnumerable<ISendEmailVerification>? sendEmailVerifications = null,
        IEnumerable<ISendUserInfo>? sendUserInfos = null)
        : base(ShiftIdentityActions.Users)
    {
        this.userRepo = userRepo;
        this.mapper = mapper;
        this.options = options;
        this.sendEmailVerifications = sendEmailVerifications;
        this.sendUserInfos = sendUserInfos;
    }

    [HttpPost("AssignRandomPasswords")]
    [TypeAuth<ShiftIdentityActions>(nameof(ShiftIdentityActions.Users), TypeAuth.Core.Access.Write)]
    public async Task<IActionResult> AssignRandomPasswords([FromBody] SelectStateDTO<UserListDTO> ids, [FromQuery(Name = "shareWithUser")] bool shareWithUser)
    {
        var decodedIds = ids.Items.Select(x => ShiftEntityHashIdService.Decode<UserDTO>(x.ID));

        var users = await userRepo.AssignRandomPasswordsAsync(decodedIds);

        var userInfos = this.mapper.Map<IEnumerable<UserInfoDTO>>(users);

        await userRepo.SaveChangesAsync();

        if (shareWithUser)
        {
            if (sendUserInfos != null)
                foreach (var sendUserInfo in sendUserInfos)
                    await sendUserInfo.SendUserInfoAsync(userInfos);
        }

        return Ok(new ShiftEntityResponse<IEnumerable<UserInfoDTO>>
        {
            Additional = new Dictionary<string, object>
            {
                ["Users"] = userInfos,
            }
        });
    }

    [HttpPost("VerifyEmails")]
    [TypeAuth<ShiftIdentityActions>(nameof(ShiftIdentityActions.Users), TypeAuth.Core.Access.Write)]
    public async Task<IActionResult> VerifyEmails([FromBody] SelectStateDTO<UserListDTO> ids)
    {
        var users = await userRepo.FindUsersAsync(ids.Items.Select(x=> x.ID.ToLong()));

        List<(User user, string fullUrl)> datas = new();

        foreach (var user in users)
        {
            if(user.EmailVerified || string.IsNullOrWhiteSpace(user.Email))
                continue;

            var encodedId = ShiftEntityHashIdService.Encode<UserDTO>(user.ID);

            // Generate the token and send the email verification
            var userManagerControllerName = nameof(UserManagerController).Replace("Controller", "");
            var url = Url.Action(nameof(UserManagerController.VerifyEmail), userManagerControllerName, 
                new { userId = encodedId });
            var uniqueId = $"{url}-{user.Email}";
            var (token, expires) = TokenService.GenerateSASToken(uniqueId, encodedId,
                DateTime.UtcNow.AddSeconds(options.SASToken.ExpiresInSeconds), options.SASToken.Key);

            // Generate the full url
            string baseUrl = $"{Request.Scheme}://{Request.Host}";
            var fullUrl = $"{baseUrl}{(baseUrl.EndsWith('/') ? baseUrl.Substring(0, baseUrl.Length - 1) : "")}{url}?expires={expires}&token={token}";

            // Save the token to the user
            user.VerificationSASToken = token;

            datas.Add((user, fullUrl));
        }

        await userRepo.SaveChangesAsync();

        foreach(var data in datas)
        {
            // Send the email verification
            if (sendEmailVerifications is not null)
                foreach (var sendEmailVerification in sendEmailVerifications)
                    await sendEmailVerification.SendEmailVerificationAsync(data.fullUrl, mapper.Map<UserDataDTO>(data.user));
        }


        return Ok(new ShiftEntityResponse<IEnumerable<UserInfoDTO>>(this.mapper.Map<IEnumerable<UserInfoDTO>>(users)));
    }

    [HttpPost("VerifyPhones")]
    [TypeAuth<ShiftIdentityActions>(nameof(ShiftIdentityActions.Users), TypeAuth.Core.Access.Write)]
    public async Task<IActionResult> VerifyPhones([FromBody] SelectStateDTO<UserListDTO> ids)
    {
        var decodedIds = ids.Items.Select(x => ShiftEntityHashIdService.Decode<UserListDTO>(x.ID));

        var users = await userRepo.VerifyPhonesAsync(decodedIds);

        var userInfos = this.mapper.Map<IEnumerable<UserListDTO>>(users);

        await userRepo.SaveChangesAsync();

        return Ok(new ShiftEntityResponse<IEnumerable<UserListDTO>> { Entity = userInfos });
    }
}