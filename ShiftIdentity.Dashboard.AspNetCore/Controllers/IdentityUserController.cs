using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftEntity.Web;
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
    private readonly IEnumerable<ISendUserInfo>? sendUserInfos;

    public IdentityUserController(UserRepository userRepo, IMapper mapper, IEnumerable<ISendUserInfo>? sendUserInfos = null)
        : base(ShiftIdentityActions.Users)
    {
        this.userRepo = userRepo;
        this.mapper = mapper;
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
        var decodedIds = ids.Items.Select(x => ShiftEntityHashIdService.Decode<UserListDTO>(x.ID));

        var users = await userRepo.VerifyEmailsAsync(decodedIds);

        var userInfos = this.mapper.Map<IEnumerable<UserListDTO>>(users);

        await userRepo.SaveChangesAsync();

        return Ok(new ShiftEntityResponse<IEnumerable<UserListDTO>> { Entity = userInfos });
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