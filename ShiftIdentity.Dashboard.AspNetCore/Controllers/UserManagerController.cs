﻿using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services.Interfaces;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;

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
        private readonly IMapper mapper;

        public UserManagerController(UserRepository userRepo, IClaimService claimService, IMapper mapper)
        {
            this.userRepo = userRepo;
            this.claimService = claimService;
            this.mapper = mapper;
        }

        // GET: api/<UserManagerController>
        [HttpGet("UserData")]
        public async Task<ShiftEntityResponse<UserDataDTO>> Get()
        {
            var loginUser = claimService.GetUser();

            var user = await userRepo.FindAsync(loginUser.ID.ToLong(), null);

            return new ShiftEntityResponse<UserDataDTO>(mapper.Map<UserDataDTO>(user));
        }

        [HttpPut("UserData")]
        public async Task<IActionResult> UpdateUserData([FromBody] UserDataDTO dto)
        {
            var loginUser = claimService.GetUser();
            User? user;

            try
            {
                user = await userRepo.UpdateUserDataAsync(dto, loginUser.ID.ToLong());
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

            return Ok(new ShiftEntityResponse<UserDataDTO>(mapper.Map<UserDataDTO>(user)));
        }

        //// POST api/<UserManagerController>
        [HttpPut("ChangePassword")]
        [Authorize(Policy = "ChangePassword")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDTO dto)
        {
            var loginUser = claimService.GetUser();

            var user = await userRepo.ChangePasswordAsync(dto, loginUser.ID.ToLong());

            if (user is null)
                return BadRequest(new ShiftEntityResponse<UserDataDTO>
                {
                    Message = new Message
                    {
                        Body = "The current password is incorrect!"
                    }
                });

            await userRepo.SaveChangesAsync();

            return Ok(new ShiftEntityResponse<UserDataDTO>(mapper.Map<UserDataDTO>(user)));
        }

    }
}
