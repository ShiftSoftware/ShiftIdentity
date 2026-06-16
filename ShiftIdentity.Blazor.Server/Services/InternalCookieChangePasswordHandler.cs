using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Helpers;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;
using ShiftSoftware.ShiftIdentity.Core.IRepositories;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Services;

/// <summary>
/// Internal hosting: changes the password against the in-process <see cref="IUserRepository"/>
/// and mints a fresh JWT via <see cref="TokenService"/> to overwrite the cookie inline.
/// </summary>
internal sealed class InternalCookieChangePasswordHandler : ICookieChangePasswordHandler
{
    private readonly IUserRepository _userRepo;
    private readonly TokenService _tokenService;
    private readonly AuthService _authService;
    private readonly IHashIdService _hashIdService;

    public InternalCookieChangePasswordHandler(IUserRepository userRepo, TokenService tokenService, AuthService authService, IHashIdService hashIdService)
    {
        _userRepo = userRepo;
        _tokenService = tokenService;
        _authService = authService;
        _hashIdService = hashIdService;
    }

    public async Task<CookieChangePasswordResult> ChangePasswordAsync(ChangePasswordDTO dto, HttpContext httpContext)
    {
        var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim))
            return new CookieChangePasswordResult(false, "Not authenticated");

        long userId;
        try { userId = _hashIdService.Decode<UserDTO>(userIdClaim); }
        catch { return new CookieChangePasswordResult(false, "Invalid user id"); }

        try
        {
            var user = await _userRepo.ChangePasswordAsync(dto, userId);
            if (user is null)
                return new CookieChangePasswordResult(false, "User not found");

            await _userRepo.SaveChangesAsync();

            // Mint a fresh JWT (now with RequirePasswordChange=false because
            // ChangePasswordAsync clears RequireChangePassword on the user entity)
            // and overwrite the cookie inline.
            var freshToken = _tokenService.GenerateInternalJwtToken(user);
            await CookieAuthHelpers.SignInWithToken(httpContext, freshToken);

            return new CookieChangePasswordResult(true, null);
        }
        catch (ShiftEntityException ex)
        {
            return new CookieChangePasswordResult(false, ex.Message?.Body);
        }
    }

    public async Task<CookieChangePasswordResult> CompletePasswordChangeAsync(CompletePasswordChangeDTO dto, HttpContext httpContext)
    {
        var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim))
            return new CookieChangePasswordResult(false, "Not authenticated");

        long userId;
        try { userId = _hashIdService.Decode<UserDTO>(userIdClaim); }
        catch { return new CookieChangePasswordResult(false, "Invalid user id"); }

        try
        {
            var freshToken = await _authService.CompletePasswordChangeAsync(userId, dto);
            if (freshToken is null)
                return new CookieChangePasswordResult(false, "Failed to change password");

            // Upgrade the challenge cookie to a full session cookie.
            await CookieAuthHelpers.SignInWithToken(httpContext, freshToken);

            return new CookieChangePasswordResult(true, null);
        }
        catch (ShiftEntityException ex)
        {
            return new CookieChangePasswordResult(false, ex.Message?.Body);
        }
    }
}
