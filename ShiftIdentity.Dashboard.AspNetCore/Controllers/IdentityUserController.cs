using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Core.Services;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;
using ShiftSoftware.TypeAuth.AspNetCore;
using ShiftSoftware.TypeAuth.Core;
using System.Net;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers;

[Route("api/[controller]")]
public class IdentityUserController : ShiftEntitySecureControllerAsync<UserRepository, User, UserListDTO, UserDTO>
{
    private readonly UserRepository userRepo;
    private readonly IMapper mapper;
    private readonly ShiftIdentityConfiguration options;
    private readonly ITypeAuthService typeAuth;
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly IHashIdService hashIdService;
    private readonly IEnumerable<ISendEmailVerification>? sendEmailVerifications;
    private readonly IEnumerable<ISendUserInfo>? sendUserInfos;

    public IdentityUserController(UserRepository userRepo,
        IMapper mapper,
        ShiftIdentityConfiguration options,
        ITypeAuthService typeAuth,
        IHttpContextAccessor httpContextAccessor,
        IHashIdService hashIdService,
        IEnumerable<ISendEmailVerification>? sendEmailVerifications = null,
        IEnumerable<ISendUserInfo>? sendUserInfos = null)
        : base(ShiftIdentityActions.Users)
    {
        this.userRepo = userRepo;
        this.mapper = mapper;
        this.options = options;
        this.typeAuth = typeAuth;
        this.httpContextAccessor = httpContextAccessor;
        this.hashIdService = hashIdService;
        this.sendEmailVerifications = sendEmailVerifications;
        this.sendUserInfos = sendUserInfos;
    }

    [HttpGet("{key}/EffectivePermissions")]
    [TypeAuth<ShiftIdentityActions>(nameof(ShiftIdentityActions.Users), TypeAuth.Core.Access.Read)]
    public async Task<IActionResult> EffectivePermissions(string key)
    {
        // AND-gate against AccessTrees Read (attribute handles Users Read).
        if (!typeAuth.CanRead(ShiftIdentityActions.AccessTrees))
            return StatusCode((int)HttpStatusCode.Forbidden, new ShiftEntityResponse<UserEffectivePermissionsDTO>
            {
                Message = new Message("Forbidden", "Read access to both Users and Access Trees is required.")
            });

        var userId = hashIdService.Decode<UserDTO>(key);

        var accessTree = await userRepo.GenerateEffectiveAccessTreeAsync(userId);

        return Ok(new ShiftEntityResponse<UserEffectivePermissionsDTO>(new UserEffectivePermissionsDTO
        {
            AccessTree = accessTree,
        }));
    }

    [HttpPost("AssignRandomPasswords")]
    [TypeAuth<ShiftIdentityActions>(nameof(ShiftIdentityActions.Users), TypeAuth.Core.Access.Write)]
    public async Task<IActionResult> AssignRandomPasswords([FromBody] SelectStateDTO<UserListDTO> ids, [FromQuery(Name = "shareWithUser")] bool shareWithUser, [FromQuery(Name = "passwordLength")] int passwordLength = 20)
    {
        var users = userRepo.AssignRandomPasswords(await this.GetSelectedEntitiesAsync(ids), passwordLength, options.Security.RequirePasswordChange);

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

    [HttpPost("ResetTotp")]
    [TypeAuth<ShiftIdentityActions>(nameof(ShiftIdentityActions.Users), TypeAuth.Core.Access.Write)]
    public async Task<IActionResult> ResetTotp([FromBody] SelectStateDTO<UserListDTO> ids)
    {
        var users = await this.GetSelectedEntitiesAsync(ids);
        foreach (var user in users)
        {
            await userRepo.SetTotpSecret(null, user);
        }

        await userRepo.SaveChangesAsync();
        return Ok(new ShiftEntityResponse<IEnumerable<UserInfoDTO>>(this.mapper.Map<IEnumerable<UserInfoDTO>>(users)));

    }

    [HttpPost("VerifyEmails")]
    [TypeAuth<ShiftIdentityActions>(nameof(ShiftIdentityActions.Users), TypeAuth.Core.Access.Write)]
    public async Task<IActionResult> VerifyEmails([FromBody] SelectStateDTO<UserListDTO> ids)
    {
        var users = await this.GetSelectedEntitiesAsync(ids);

        List<(User user, string fullUrl)> datas = new();

        foreach (var user in users)
        {
            if(user.EmailVerified || string.IsNullOrWhiteSpace(user.Email))
                continue;

            var encodedId = hashIdService.Encode<UserDTO>(user.ID);

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
        var users = userRepo.VerifyPhonesAsync(await this.GetSelectedEntitiesAsync(ids));

        var userInfos = this.mapper.Map<IEnumerable<UserListDTO>>(users);

        await userRepo.SaveChangesAsync();

        return Ok(new ShiftEntityResponse<IEnumerable<UserListDTO>> { Entity = userInfos });
    }

    [HttpPost("ImportUsers")]
    [TypeAuth<ShiftIdentityActions>(nameof(ShiftIdentityActions.Users), TypeAuth.Core.Access.Write)]
    public async Task<IActionResult> ImportUsers([FromBody] UserImportDTO userImport)
    {
        try
        {
            var selfBranchId = httpContextAccessor.HttpContext?.GetHashedCompanyBranchID()!;
            foreach (var user in userImport.Users)
                if(!typeAuth.CanRead(ShiftIdentityActions.DataLevelAccess.Branches, user.CompanyBranchID, selfBranchId))
                    throw new ShiftEntityException(new Message("Error", "Unauthorized"), (int)System.Net.HttpStatusCode.Forbidden);

            var users = await userRepo.UserImportAsync(userImport.Users);

            await userRepo.SaveChangesAsync();

            if (userImport.SendLoginInfoByEmail)
                if (sendUserInfos is not null)
                    foreach (var sendUserInfo in sendUserInfos)
                        await sendUserInfo.SendUserInfoAsync(users.Select(x=> new UserInfoDTO
                        {
                            Username = x.Username,
                            PlainTextPassword = x.Password,
                            Email = x.Email,
                        }));
        }
        catch (ShiftEntityException ex)
        {
            return StatusCode(ex.HttpStatusCode, new ShiftEntityResponse<UserImportDTO>
            {
                Message = ex.Message,
                Additional = ex.AdditionalData,
            });
        }

        return Ok(new ShiftEntityResponse<UserImportDTO> { Entity = userImport });
    }
}