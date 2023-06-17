using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services.Interfaces;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UserManagerController : ControllerBase
    {
        private readonly UserRepository userRepo;
        private readonly IClaimService claimService;

        public UserManagerController(UserRepository userRepo, IClaimService claimService)
        {
            this.userRepo = userRepo;
            this.claimService = claimService;
        }

        // GET: api/<UserManagerController>
        [HttpGet("UserData")]
        public async Task<ShiftEntityResponse<UserDataDTO>> Get()
        {
            var loginUser = claimService.GetUser();

            var user = await userRepo.FindAsync(loginUser.ID);

            return new ShiftEntityResponse<UserDataDTO>((UserDataDTO)user);
        }

        [HttpPut("UserData")]
        public async Task<IActionResult> UpdateUserData([FromBody] UserDataDTO dto)
        {
            var loginUser = claimService.GetUser();
            User? user;

            try
            {
                user = await userRepo.UpdateUserDataAsync(dto, loginUser.ID);
            }
            catch (ShiftEntityException ex)
            {
                return StatusCode(ex.HttpStatusCode, new ShiftEntityResponse<UserDataDTO>
                {
                    Message = ex.Message
                });
            }

            if (user is null)
                return BadRequest(new ShiftEntityResponse<UserDataDTO>
                {
                    Message = new Message
                    {
                        Body = "Could not update user data!"
                    }
                });

            await userRepo.SaveChangesAsync();

            return Ok(new ShiftEntityResponse<UserDataDTO>((UserDataDTO)user));
        }

        //// POST api/<UserManagerController>
        [HttpPut("ChangePassword")]
        [Authorize(Policy = "ChangePassword")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDTO dto)
        {
            var loginUser = claimService.GetUser();

            var user = await userRepo.ChangePasswordAsync(dto, loginUser.ID);

            if (user is null)
                return BadRequest(new ShiftEntityResponse<UserDataDTO>
                {
                    Message = new Message
                    {
                        Body = "The current password is incorrect!"
                    }
                });

            await userRepo.SaveChangesAsync();

            return Ok(new ShiftEntityResponse<UserDataDTO>((UserDataDTO)user));
        }

    }
}
