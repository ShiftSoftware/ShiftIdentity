using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services.Interfaces;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Models;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Controllers.API;

[Route("api/[controller]")]
[ApiController]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly AuthService authService;
    private readonly AuthCodeService authCodeService;
    private readonly TokenService tokenService;
    private readonly IClaimService claimService;

    public AuthController(
            AuthService authService,
            AuthCodeService authCodeService,
            TokenService tokenService,
            IClaimService claimService
        )
    {
        this.authService = authService;
        this.authCodeService = authCodeService;
        this.tokenService = tokenService;
        this.claimService = claimService;
    }

    [HttpPost("Login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginDTO loginDto)
    {
        var result = await authService.LoginAsync(loginDto);

        if (result.Result != LoginResultEnum.Success)
            return BadRequest(new ShiftEntityResponse<TokenDTO>
            {
                Message = new Message
                {
                    Body = result.ErrorMessage
                }
            });

        return Ok(new ShiftEntityResponse<TokenDTO>(result.Token));
    }

    [HttpPost("Refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshDTO dto)
    {
        var token = await tokenService.RefreshAsync(dto.RefreshToken);

        if (token is null)
            return Unauthorized(new ShiftEntityResponse<TokenDTO>
            {
                Message = new Message
                {
                    Body = "Invalid refresh token"
                }
            });

        return Ok(new ShiftEntityResponse<TokenDTO>(token));
    }

    /// <summary>
    /// Code challenge must be hashed with SHA512 algorithm and then converted to Hex string
    /// </summary>
    /// <param name="generateAuthCodeDto"></param>
    /// <returns></returns>
    [HttpPost("AuthCode")]
    public async Task<IActionResult> GenerateAuthCode([FromBody] GenerateAuthCodeDTO generateAuthCodeDto)
    {
        var loginUser = claimService.GetUser();

        var authCode = await authCodeService.GenerateCodeAsync(generateAuthCodeDto, loginUser.ID);

        if (authCode is null)
            return BadRequest(new ShiftEntityResponse<AuthCodeDTO>
            {
                Message = new Message
                {
                    Body = "Failed to genearate auth-code"
                }
            });

        var authCodeDto = new AuthCodeDTO
        {
            AppDisplayName = authCode.AppDisplayName,
            Code = authCode.Code,
            ReturnUrl = generateAuthCodeDto.ReturnUrl,
            RedirectUri = authCode.RedirectUri
        };

        return Ok(new ShiftEntityResponse<AuthCodeDTO>(authCodeDto));
    }

    /// <summary>
    /// Code verifier is checked against SHA512 hash algorithm
    /// </summary>
    /// <param name="dto"></param>
    /// <returns></returns>
    [HttpPost("TokenWithAppIdOnly")]
    [AllowAnonymous]
    public async Task<IActionResult> GenereateExternalTokenWithAppIdOnly([FromBody] GenerateExternalTokenWithAppIdOnlyDTO dto)
    {
        var token = await authService.GenrerateExternalTokenWithAppIdOnly(dto);

        if (token is null)
            return BadRequest(new ShiftEntityResponse<TokenDTO>
            {
                Message = new Message
                {
                    Body = "Failed to genearate token"
                }
            });

        return Ok(new ShiftEntityResponse<TokenDTO>(token));
    }
}
