using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Core.Services;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services.Interfaces;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authorization;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using TotpService = ShiftSoftware.ShiftIdentity.AspNetCore.Services.TotpService;
using AuthTokenService = ShiftSoftware.ShiftIdentity.AspNetCore.Services.TokenService;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Endpoints;

// The 9 account/verification endpoints ported verbatim from UserManagerController (all on the api/UserManager
// prefix; routes + verbs byte-identical). Class-level [Authorize] → .RequireAuthorization() on the authenticated
// endpoints; per-action [AllowAnonymous] → .AllowAnonymous(); [StepUp(purpose)] → .RequireAuthorization(
// StepUpPolicy.For(purpose, allowAccessToken: true)) (true is the attribute's default). VerifyEmail and
// ResetPassword are NAMED endpoints so the SAS-token URLs (previously built via Url.Action) reproduce byte-for-byte
// through LinkGenerator.GetPathByName — old email/reset links keep validating. Composed by MapShiftIdentityDashboard().
internal static class UserManagerEndpoints
{
    // Endpoint names for the token-keyed URLs. The SAS uniqueId is derived from the generated path, so the
    // link-issuing endpoint and the validating endpoint MUST resolve the same string — hence a shared name.
    // (Also referenced by UserEndpoints.VerifyEmails.)
    public const string VerifyEmailRouteName = "ShiftIdentity_UserManager_VerifyEmail";
    public const string ResetPasswordRouteName = "ShiftIdentity_UserManager_ResetPassword";

    public static IEndpointRouteBuilder MapUserManagerEndpoints(this IEndpointRouteBuilder app)
    {
        // GET api/UserManager/UserData — the current user's own data.
        app.MapGet("api/UserManager/UserData",
            async (IClaimService claimService, UserRepository userRepo, IMapper mapper) =>
            {
                var loginUser = claimService.GetUser();
                var user = await userRepo.FindAsync(loginUser.ID.ToLong(), null, disableDefaultDataLevelAccess: true, disableGlobalFilters: true);
                return new ShiftEntityResponse<UserDataDTO>(mapper.Map<UserDataDTO>(user));
            })
            .RequireAuthorization();

        // PUT api/UserManager/UserData — update the current user's own data.
        app.MapPut("api/UserManager/UserData",
            async (UserDataDTO dto, IClaimService claimService, UserRepository userRepo, IMapper mapper) =>
            {
                var loginUser = claimService.GetUser();
                User? user;

                try
                {
                    user = await userRepo.UpdateUserDataAsync(dto, loginUser.ID.ToLong());
                }
                catch (ShiftEntityException ex)
                {
                    return Results.Json(new ShiftEntityResponse<UserDataDTO> { Message = ex.Message }, statusCode: ex.HttpStatusCode);
                }

                if (user is null)
                    return Results.BadRequest(new ShiftEntityResponse<UserDataDTO> { Message = new Message { Body = "Could not update user data!" } });

                await userRepo.SaveChangesAsync();

                return Results.Ok(new ShiftEntityResponse<UserDataDTO>(mapper.Map<UserDataDTO>(user)));
            })
            .RequireAuthorization();

        // PUT api/UserManager/ChangePassword — step-up (ChangePassword purpose). Returns a fresh login token.
        app.MapPut("api/UserManager/ChangePassword",
            async (ChangePasswordDTO dto, IClaimService claimService, UserRepository userRepo, AuthTokenService authTokenService) =>
            {
                var loginUser = claimService.GetUser();
                User? user;

                try
                {
                    user = await userRepo.ChangePasswordAsync(dto, loginUser.ID.ToLong());
                }
                catch (ShiftEntityException ex)
                {
                    return Results.Json(new ShiftEntityResponse<UserDataDTO> { Message = ex.Message }, statusCode: ex.HttpStatusCode);
                }

                if (user is null)
                    return Results.BadRequest(new ShiftEntityResponse<UserDataDTO> { Message = new Message { Body = "Could not update user data!" } });

                await userRepo.SaveChangesAsync();

                var token = authTokenService.IssueLoginToken(user);
                return Results.Ok(new ShiftEntityResponse<TokenDTO>(token!));
            })
            .RequireAuthorization(StepUpPolicy.For(AuthPurpose.ChangePassword, allowAccessToken: true));

        // GET api/UserManager/SendEmailVerificationLink — emails the current user a SAS-token verification link.
        app.MapGet("api/UserManager/SendEmailVerificationLink",
            async (HttpContext httpContext, IClaimService claimService, UserRepository userRepo, IMapper mapper,
                   IHashIdService hashIdService, ShiftIdentityConfiguration options, LinkGenerator linkGenerator,
                   IEnumerable<ISendEmailVerification>? sendEmailVerifications) =>
            {
                var loginUser = claimService.GetUser();
                var userId = loginUser.ID.ToLong();
                var encodedId = hashIdService.Encode<UserDTO>(userId);

                var user = await userRepo.FindAsync(userId, asOf: null, disableDefaultDataLevelAccess: true, disableGlobalFilters: true);
                if (user is null)
                    return Results.BadRequest(new ShiftEntityResponse<UserDataDTO> { Message = new Message { Body = "User not found!" } });

                if (string.IsNullOrWhiteSpace(user.Email))
                    return Results.BadRequest(new ShiftEntityResponse<UserDataDTO> { Message = new Message { Body = "User email is not found!" } });

                if (user.EmailVerified)
                    return Results.BadRequest(new ShiftEntityResponse<UserDataDTO> { Message = new Message { Body = "User email is already verified!" } });

                // Named endpoint → same path Url.Action produced, so the SAS uniqueId matches VerifyEmail's.
                var url = linkGenerator.GetPathByName(httpContext, VerifyEmailRouteName, new { userId = encodedId });
                var uniqueId = $"{url}-{user.Email}";
                var (token, expires) = TokenService.GenerateSASToken(uniqueId, encodedId, DateTime.UtcNow.AddSeconds(options.SASToken.ExpiresInSeconds), options.SASToken.Key);

                string baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
                var fullUrl = $"{baseUrl}{(baseUrl.EndsWith('/') ? baseUrl.Substring(0, baseUrl.Length - 1) : "")}{url}?expires={expires}&token={token}";

                user.VerificationSASToken = token;
                await userRepo.SaveChangesAsync();

                if (sendEmailVerifications is not null)
                    foreach (var sendEmailVerification in sendEmailVerifications)
                        await sendEmailVerification.SendEmailVerificationAsync(fullUrl, mapper.Map<UserDataDTO>(user));

                return Results.Ok();
            })
            .RequireAuthorization();

        // GET api/UserManager/VerifyEmail/{userId} — anonymous; hit from the email link. Named for GetPathByName.
        app.MapGet("api/UserManager/VerifyEmail/{userId}",
            async (string userId, HttpContext httpContext, UserRepository userRepo, IHashIdService hashIdService,
                   ShiftIdentityConfiguration options, LinkGenerator linkGenerator,
                   [FromQuery] string? expires, [FromQuery] string? token) =>
            {
                var decodedId = hashIdService.Decode<UserDTO>(userId);

                var user = await userRepo.FindAsync(decodedId, asOf: null, disableDefaultDataLevelAccess: true, disableGlobalFilters: true);
                if (user is null)
                    return Results.Ok("User is not found");

                var url = linkGenerator.GetPathByName(httpContext, VerifyEmailRouteName, new { userId = userId });
                if (!TokenService.ValidateSASToken($"{url}-{user.Email}", userId.ToString(), expires!, token!, options.SASToken.Key))
                    return Results.Ok("The operation is failed, the token may be expired or currupted, please retry the operation.");

                if (!TokenService.ValidateSASToken(user.VerificationSASToken ?? "", token ?? ""))
                    return Results.Ok("The operation is failed, the token may be expired or currupted, please retry the operation.");

                if (user.EmailVerified)
                    return Results.Ok("The email is already verified.");

                user.EmailVerified = true;
                user.VerificationSASToken = null;
                await userRepo.SaveChangesAsync();

                return Results.Ok("Youre email is verified successfully.");
            })
            .AllowAnonymous()
            .WithName(VerifyEmailRouteName);

        // GET api/UserManager/SendPasswordResetLink — anonymous; emails a reset link to a verified account.
        app.MapGet("api/UserManager/SendPasswordResetLink",
            async ([FromQuery] string email, HttpContext httpContext, UserRepository userRepo, IMapper mapper,
                   IHashIdService hashIdService, ShiftIdentityConfiguration options, LinkGenerator linkGenerator,
                   IEnumerable<ISendEmailResetPassword>? sendEmailResetPasswords) =>
            {
                var user = await userRepo.GetUserByEmailAsync(email);
                if (user is null)
                    return Results.NotFound(new ShiftEntityResponse<UserDataDTO> { Message = new Message { Body = "User not found!" } });

                if (!user.EmailVerified)
                    return Results.BadRequest(new ShiftEntityResponse<UserDataDTO> { Message = new Message { Body = "User email is not verified!" } });

                var encodedId = hashIdService.Encode<UserDTO>(user.ID);

                // The SAS uniqueId keys off the ResetPassword endpoint's path (matches ResetPassword's validation).
                var url = linkGenerator.GetPathByName(httpContext, ResetPasswordRouteName, new { userId = encodedId });
                var uniqueId = $"{url}-{user.Email}";
                var (token, expires) = TokenService.GenerateSASToken(uniqueId, encodedId,
                    DateTime.UtcNow.AddSeconds(options.SASToken.ExpiresInSeconds), options.SASToken.Key);

                // The emailed link points at the FRONTEND reset page, not this API endpoint.
                string apiBaseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
                string baseUrl = options.FrontEndUrl ?? apiBaseUrl;
                var fullUrl = $"{baseUrl}{(baseUrl.EndsWith('/') ? baseUrl.Substring(0, baseUrl.Length - 1) : "")}/Identity/ResetPassword/{encodedId}?expires={expires}&token={token}";

                user.VerificationSASToken = token;
                await userRepo.SaveChangesAsync();

                if (sendEmailResetPasswords is not null)
                    foreach (var sendEmailResetPassword in sendEmailResetPasswords)
                        await sendEmailResetPassword.SendEmailResetPasswordAsync(fullUrl, mapper.Map<UserDataDTO>(user));

                return Results.Ok();
            })
            .AllowAnonymous();

        // POST api/UserManager/ResetPassword/{userId} — anonymous; consumes the reset link. Named for GetPathByName.
        app.MapPost("api/UserManager/ResetPassword/{userId}",
            async (string userId, ResetPasswordDTO dto, HttpContext httpContext, UserRepository userRepo,
                   IHashIdService hashIdService, ShiftIdentityConfiguration options, LinkGenerator linkGenerator,
                   [FromQuery] string? expires, [FromQuery] string? token) =>
            {
                var decodedId = hashIdService.Decode<UserDTO>(userId);
                var user = await userRepo.FindAsync(decodedId, asOf: null, disableDefaultDataLevelAccess: true, disableGlobalFilters: true);
                if (user is null)
                    return Results.NotFound(new ShiftEntityResponse<UserDataDTO> { Message = new Message { Body = "User not found!" } });

                var url = linkGenerator.GetPathByName(httpContext, ResetPasswordRouteName, new { userId = userId });
                if (!TokenService.ValidateSASToken($"{url}-{user.Email}", userId, expires!, token!, options.SASToken.Key))
                    return Results.BadRequest(new ShiftEntityResponse<UserDataDTO> { Message = new Message { Body = "The operation is failed, the token may be expired or currupted, please retry the operation." } });

                if (!TokenService.ValidateSASToken(user.VerificationSASToken ?? "", token ?? ""))
                    return Results.BadRequest(new ShiftEntityResponse<UserDataDTO> { Message = new Message { Body = "The operation is failed, the token may be expired or currupted, please retry the operation." } });

                var hash = HashService.GenerateHash(dto.NewPassword);
                user.PasswordHash = hash.PasswordHash;
                user.Salt = hash.Salt;
                user.VerificationSASToken = null;
                // The user just chose this password themselves via the reset link, so don't force
                // another change on next login.
                user.RequireChangePassword = false;
                await userRepo.SaveChangesAsync();

                return Results.Ok(new ShiftEntityResponse<UserDataDTO> { Message = new Message { Body = "Your password is reset successfully." } });
            })
            .AllowAnonymous()
            .WithName(ResetPasswordRouteName);

        // GET api/UserManager/StartTotpEnrollment — step-up (MfaEnrollment). Returns a signed TOTP secret + QR.
        app.MapGet("api/UserManager/StartTotpEnrollment",
            (HttpContext httpContext, TotpService totpService, ShiftIdentityConfiguration options) =>
            {
                var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId is null)
                    return Results.BadRequest(new ShiftEntityResponse<UserDataDTO> { Message = new Message { Body = "Invalid token." } });

                var user = httpContext.User.FindFirstValue(ClaimTypes.Name);
                var (secret, uri) = totpService.GenerateSecret(user ?? userId);

                // Sign the secret (bound to the user + an expiry) so it can't be swapped in transit
                // before the client echoes it back on confirmation.
                var (sasToken, expires) = TokenService.GenerateSASToken(
                    secret, userId, DateTime.UtcNow.AddSeconds(options.SASToken.ExpiresInSeconds), options.SASToken.Key);

                return Results.Ok(new ShiftEntityResponse<TotpDTO>(new TotpDTO
                {
                    Uri = uri,
                    Secret = secret,
                    Svg = totpService.GenerateQrCode(uri),
                    SasToken = sasToken,
                    Expires = expires,
                }));
            })
            .RequireAuthorization(StepUpPolicy.For(AuthPurpose.MfaEnrollment, allowAccessToken: true));

        // POST api/UserManager/ConfirmTotpEnrollment — step-up (MfaEnrollment). Validates the code + persists.
        app.MapPost("api/UserManager/ConfirmTotpEnrollment",
            async (TotpDTO dto, HttpContext httpContext, UserRepository userRepo, IHashIdService hashIdService,
                   TotpService totpService, AuthTokenService authTokenService, ShiftIdentityConfiguration options) =>
            {
                var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId is null)
                    return Results.BadRequest(new ShiftEntityResponse<UserDataDTO> { Message = new Message { Body = "Invalid token." } });

                // Reject a secret that we didn't issue for this user (e.g. tampered in transit).
                if (!TokenService.ValidateSASToken(dto.Secret, userId, dto.Expires, dto.SasToken, options.SASToken.Key))
                    return Results.BadRequest(new ShiftEntityResponse<UserDataDTO> { Message = new Message { Body = "Invalid token." } });

                if (totpService.Validate(dto.Code, dto.Secret))
                {
                    var user = await userRepo.SetTotpSecret(totpService.DecodeSecret(dto.Secret), hashIdService.Decode<UserDTO>(userId));
                    await userRepo.SaveChangesAsync();
                    var token = authTokenService.IssueLoginToken(user, true);
                    return Results.Ok(new ShiftEntityResponse<TokenDTO>(token!));
                }

                return Results.BadRequest(new ShiftEntityResponse<UserDataDTO> { Message = new Message { Body = "Invalid code." } });
            })
            .RequireAuthorization(StepUpPolicy.For(AuthPurpose.MfaEnrollment, allowAccessToken: true));

        return app;
    }
}
