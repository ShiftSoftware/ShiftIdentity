using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftIdentity.AspNetCore.Filters;
using ShiftSoftware.ShiftIdentity.AspNetCore.Models;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.Security.Claims;

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
            return Unauthorized(new ShiftEntityResponse<TokenDTO>
            {
                Message = new Message
                {
                    Body = Loc["Invalid refresh token"]
                }
            });

        return Ok(new ShiftEntityResponse<TokenDTO>(token));
    }

    /// <summary>
    /// Completes the forced-password-change flow. Caller must present a challenge token
    /// (RequirePasswordChange=true claim). On success returns a full session token.
    /// </summary>
    [HttpPost("CompletePasswordChange")]
    [AllowWhenPasswordChangeRequired]
    public async Task<IActionResult> CompletePasswordChange([FromBody] CompletePasswordChangeDTO dto)
    {
        // Defense in depth: the filter allow-lists this endpoint, so any authenticated caller
        // could reach it. Require the challenge claim explicitly so a normal session can't use
        // this endpoint to skip the current-password check.
        var requireClaim = User.FindFirstValue(ShiftIdentityClaims.RequirePasswordChange);
        if (!bool.TryParse(requireClaim, out var requireChange) || !requireChange)
            return Forbid();

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim))
            return Unauthorized();

        long userId;
        try { userId = ShiftEntityHashIdService.Decode<UserDTO>(userIdClaim); }
        catch { return Unauthorized(); }

        var token = await authService.CompletePasswordChangeAsync(userId, dto);
        if (token is null)
            return BadRequest(new ShiftEntityResponse<TokenDTO>
            {
                Message = new Message { Body = Loc["Failed to change password"] }
            });

        return Ok(new ShiftEntityResponse<TokenDTO>(token));
    }

}
