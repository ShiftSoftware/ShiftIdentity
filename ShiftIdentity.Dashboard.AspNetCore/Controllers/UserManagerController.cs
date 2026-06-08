using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Core;
using Microsoft.AspNetCore.RateLimiting;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.AspNetCore;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services.Interfaces;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;
// two very different classes share the same name
using SASTokenService = ShiftSoftware.ShiftEntity.Core.Services.TokenService;
using IdentityTokenService = ShiftSoftware.ShiftIdentity.AspNetCore.Services.TokenService;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    [EnableRateLimiting(Core.Constants.DefaultPolicyName)]
    public class UserManagerController : ControllerBase
    {
        private readonly UserRepository userRepo;
        private readonly IClaimService claimService;
        private readonly IMapper mapper;
        private readonly ShiftIdentityConfiguration options;
        private readonly IHashIdService hashIdService;
        private readonly IdentityTokenService tokenService;
        private readonly IEnumerable<ISendEmailVerification>? sendEmailVerifications;
        private readonly IEnumerable<ISendEmailResetPassword>? sendEmailResetPasswords;

        public UserManagerController(UserRepository userRepo, IClaimService claimService, IMapper mapper,
            ShiftIdentityConfiguration options, IHashIdService hashIdService, IdentityTokenService tokenService,
            IEnumerable<ISendEmailVerification>? sendEmailVerifications = null,
            IEnumerable<ISendEmailResetPassword>? sendEmailResetPasswords = null)
        {
            this.userRepo = userRepo;
            this.claimService = claimService;
            this.mapper = mapper;
            this.options = options;
            this.hashIdService = hashIdService;
            this.tokenService = tokenService;
            this.sendEmailVerifications = sendEmailVerifications;
            this.sendEmailResetPasswords = sendEmailResetPasswords;
        }

        // GET: api/<UserManagerController>
        [HttpGet("UserData")]
        public async Task<ShiftEntityResponse<UserDataDTO>> Get()
        {
            var loginUser = claimService.GetUser();

            var user = await userRepo.FindAsync(loginUser.ID.ToLong(), null, disableDefaultDataLevelAccess: true, disableGlobalFilters: true);

            return new ShiftEntityResponse<UserDataDTO>(mapper.Map<UserDataDTO>(user));
        }

        [HttpPut("UserData")]
        public async Task<IActionResult> UpdateUserDataJson([FromBody] UserDataDTO dto)
        {
            User? user;

            try
            {
                user = await UpdateUserData(dto);
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

            return Ok(new ShiftEntityResponse<UserDataDTO>(mapper.Map<UserDataDTO>(user)));
        }

        [HttpPost("UserData")]
        [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
        public async Task<IActionResult> UpdateUserDataForm([FromForm] UserDataDTO dto)
        {
            User? user;

            try
            {
                user = await UpdateUserData(dto);
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

            return Ok(new ShiftEntityResponse<UserDataDTO>(mapper.Map<UserDataDTO>(user)));
        }

        //// POST api/<UserManagerController>
        [HttpPut("ChangePassword")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDTO dto)
        {
            var loginUser = claimService.GetUser();
            User? user;

            try
            {
                user = await userRepo.ChangePasswordAsync(dto, loginUser.ID.ToLong());
            }
            catch (ShiftEntityException ex)
            {
                return StatusCode(ex.HttpStatusCode, new ShiftEntityResponse<TokenDTO>
                {
                    Message = ex.Message
                });
            }

            if (user is null)
                return BadRequest(new ShiftEntityResponse<TokenDTO>
                {
                    Message = new Message
                    {
                        Body = "Could not update user data!"
                    }
                });

            await userRepo.SaveChangesAsync();

            // If the user opted to sign out their other sessions, ChangePasswordAsync rolled the
            // SecurityStamp, invalidating refresh tokens issued before this call. Re-issue a token
            // carrying the current stamp so the session that just changed the password stays alive.
            var token = tokenService.GenerateInternalJwtToken(user);

            return Ok(new ShiftEntityResponse<TokenDTO>(token));
        }

        [HttpGet("SendEmailVerificationLink")]
        public async Task<IActionResult> SendEmailVerificationLink()
        {
            var loginUser = claimService.GetUser();
            var userId = loginUser.ID.ToLong();
            var encodedId = hashIdService.Encode<UserDTO>(userId);

            // Get the user and check if the user is not null
            var user = await userRepo.FindAsync(userId, asOf: null, disableDefaultDataLevelAccess: true, disableGlobalFilters: true);
            if (user is null)
                return BadRequest(new ShiftEntityResponse<UserDataDTO>
                {
                    Message = new Message
                    {
                        Body = "User not found!"
                    }
                });

            //Check if the email is null
            if (string.IsNullOrWhiteSpace(user.Email))
                return BadRequest(new ShiftEntityResponse<UserDataDTO>
                {
                    Message = new Message
                    {
                        Body = "User email is not found!"
                    }
                });

            // Check if the user is already verified
            if (user.EmailVerified)
                return BadRequest(new ShiftEntityResponse<UserDataDTO>
                {
                    Message = new Message
                    {
                        Body = "User email is already verified!"
                    }
                });

            // Generate the token and send the email verification
            var url = Url.Action(nameof(VerifyEmail), new { userId = encodedId });
            var uniqueId = $"{url}-{user.Email}";
            var (token, expires) = SASTokenService.GenerateSASToken(uniqueId, encodedId, DateTime.UtcNow.AddSeconds(options.SASToken.ExpiresInSeconds), options.SASToken.Key);

            // Generate the full url
            string baseUrl = $"{Request.Scheme}://{Request.Host}";
            var fullUrl = $"{baseUrl}{(baseUrl.EndsWith('/') ? baseUrl.Substring(0, baseUrl.Length - 1) : "")}{url}?expires={expires}&token={token}";

            // Save the token to the user
            user.VerificationSASToken = token;
            await userRepo.SaveChangesAsync();

            // Send the email verification
            if (sendEmailVerifications is not null)
                foreach (var sendEmailVerification in sendEmailVerifications)
                    await sendEmailVerification.SendEmailVerificationAsync(fullUrl, mapper.Map<UserDataDTO>(user));

            return Ok();
        }

        [HttpGet("VerifyEmail/{userId}")]
        [AllowAnonymous]
        public async Task<IActionResult> VerifyEmail(string userId, [FromQuery] string? expires = null, [FromQuery] string? token = null)
        {
            var decodedId = hashIdService.Decode<UserDTO>(userId);

            // Get the user and check if the user is not null
            var user = await userRepo.FindAsync(decodedId, asOf: null, disableDefaultDataLevelAccess: true, disableGlobalFilters: true);
            if (user is null)
                return Ok("User is not found");

            // Verify the token
            var url = Url.Action(nameof(VerifyEmail), new { userId = userId });
            if (!SASTokenService.ValidateSASToken($"{url}-{user.Email}", userId.ToString(), expires!, token!, options.SASToken.Key))
                return Ok("The operation is failed, the token may be expired or currupted, please retry the operation.");

            // Verify the token against the database
            if (!SASTokenService.ValidateSASToken(user.VerificationSASToken ?? "", token ?? ""))
                return Ok("The operation is failed, the token may be expired or currupted, please retry the operation.");

            // Check if the email is already verified
            if (user.EmailVerified)
                return Ok("The email is already verified.");

            // Verify the email
            user.EmailVerified = true;
            user.VerificationSASToken = null;
            await userRepo.SaveChangesAsync();

            return Ok("Youre email is verified successfully.");
        }

        [HttpGet("SendPasswordResetLink")]
        [AllowAnonymous]
        public async Task<IActionResult> SendPasswordResetLink([FromQuery] string email)
        {
            // Get the user and check if the user is not null
            var user = await userRepo.GetUserByEmailAsync(email);
            if (user is null)
                return NotFound(new ShiftEntityResponse<UserDataDTO>
                {
                    Message = new Message
                    {
                        Body = "User not found!"
                    }
                });

            //Check if the email is verified
            if (!user.EmailVerified)
                return BadRequest(new ShiftEntityResponse<UserDataDTO>
                {
                    Message = new Message
                    {
                        Body = "User email is not verified!"
                    }
                });

            var encodedId = hashIdService.Encode<UserDTO>(user.ID);
            
            // Generate the token and send the email verification
            var url = Url.Action(nameof(ResetPassword), new { userId = encodedId });
            var uniqueId = $"{url}-{user.Email}";
            var (token, expires) = SASTokenService.GenerateSASToken(uniqueId, encodedId, 
                DateTime.UtcNow.AddSeconds(options.SASToken.ExpiresInSeconds), options.SASToken.Key);

            // Generate the full url
            string apiBaseUrl = $"{Request.Scheme}://{Request.Host}";
            string baseUrl = options.FrontEndUrl ?? apiBaseUrl;
            var fullUrl = $"{baseUrl}{(baseUrl.EndsWith('/') ? baseUrl.Substring(0, baseUrl.Length - 1) : "")}/Identity/ResetPassword/{encodedId}?expires={expires}&token={token}";

            // Save the token to the user
            user.VerificationSASToken = token;
            await userRepo.SaveChangesAsync();

            // Send the email verification
            if (sendEmailResetPasswords is not null)
                foreach (var sendEmailResetPassword in sendEmailResetPasswords)
                    await sendEmailResetPassword.SendEmailResetPasswordAsync(fullUrl, mapper.Map<UserDataDTO>(user));

            return Ok();
        }

        [HttpPost("ResetPassword/{userId}")]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword(string userId, [FromBody] ResetPasswordDTO dto,
            [FromQuery] string? expires = null, [FromQuery] string? token = null)
        {
            // Get the user and check if the user is not null
            var decodedId = hashIdService.Decode<UserDTO>(userId);
            var user = await userRepo.FindAsync(decodedId, asOf: null, disableDefaultDataLevelAccess: true, disableGlobalFilters: true);
            if (user is null)
                return NotFound(new ShiftEntityResponse<UserDataDTO>
                {
                    Message = new Message
                    {
                        Body = "User not found!"
                    }
                });

            // Verify the token
            var url = Url.Action(nameof(ResetPassword), new { userId = userId });
            if (!SASTokenService.ValidateSASToken($"{url}-{user.Email}", userId, expires!, token!, options.SASToken.Key))
                return BadRequest(new ShiftEntityResponse<UserDataDTO>
                {
                    Message = new Message
                    {
                        Body = "The operation is failed, the token may be expired or currupted, please retry the operation."
                    }
                });

            // Verify the token against the database
            if (!SASTokenService.ValidateSASToken(user.VerificationSASToken ?? "", token ?? ""))
                return BadRequest(new ShiftEntityResponse<UserDataDTO>
                {
                    Message = new Message
                    {
                        Body = "The operation is failed, the token may be expired or currupted, please retry the operation."
                    }
                });

            // Reset the password
            var hash = HashService.GenerateHash(dto.NewPassword);
            user.PasswordHash = hash.PasswordHash;
            user.Salt = hash.Salt;
            user.VerificationSASToken = null;
            await userRepo.SaveChangesAsync();

            return Ok(new ShiftEntityResponse<UserDataDTO>
            {
                Message = new Message
                {
                    Body = "Your password is reset successfully."
                }
            });
        }

        [NonAction]
        private async Task<User?> UpdateUserData(UserDataDTO dto)
        {
            var loginUser = claimService.GetUser();
            var user = await userRepo.UpdateUserDataAsync(dto, loginUser.ID.ToLong());

            if (user is null)
                return null;

            await userRepo.SaveChangesAsync();

            return user;
        }
    }
}
