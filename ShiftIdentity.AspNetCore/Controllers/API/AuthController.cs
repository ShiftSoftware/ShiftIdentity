using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.AspNetCore.Models;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Localization;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Controllers.API;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class AuthController : ControllerBase
{
    private readonly AuthService authService;
    private readonly ShiftIdentityLocalizer Loc;

    public AuthController(
            AuthService authService,
            ShiftIdentityLocalizer Loc
        )
    {
        this.authService = authService;
        this.Loc = Loc;
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
        // Set security headers to prevent caching.
        Response.Headers["Cache-Control"] = "no-store, no-cache";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";

        var token = await authService.RefreshAsync(dto.RefreshToken);

        if (token is null)
            return BadRequest(new ShiftEntityResponse<TokenDTO>
            {
                Message = new Message
                {
                    Body = Loc["Invalid refresh token"]
                }
            });

        return Ok(new ShiftEntityResponse<TokenDTO>(token));
    }

}
