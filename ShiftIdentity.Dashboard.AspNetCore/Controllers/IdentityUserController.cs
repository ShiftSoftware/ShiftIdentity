using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;
using ShiftSoftware.TypeAuth.AspNetCore;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers
{
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

        [HttpPost("ResetRandomPasswords")]
        [TypeAuth<ShiftIdentityActions>(nameof(ShiftIdentityActions.Users),TypeAuth.Core.Access.Write)]
        public async Task<IActionResult> ResetRandomPassword([FromBody] IEnumerable<string> ids)
        {
            var decodedIds = ids.ToList().Select(x => ShiftEntityHashIdService.Decode<UserDTO>(x));

            var users = await userRepo.ResetRandomPasswordsAsync(decodedIds);

            var userInfos = this.mapper.Map<IEnumerable<UserInfoDTO>>(users);

            await userRepo.SaveChangesAsync();

            if (sendUserInfos != null)
                foreach (var sendUserInfo in sendUserInfos)
                    await sendUserInfo.SendUserInfoAsync(userInfos);

            return Ok(userInfos);
        }
    }
}
