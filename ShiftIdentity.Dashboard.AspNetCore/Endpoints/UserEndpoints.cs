using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Core.Services;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;
using ShiftSoftware.TypeAuth.AspNetCore.EndpointFilters;
using ShiftSoftware.TypeAuth.Core;
using System.Net;
using AutoMapper;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Endpoints;

// The User feature's CUSTOM (non-CRUD) endpoints — the 6 that used to live on IdentityUserController alongside its
// base CRUD. Base CRUD is now attribute-driven (on the User entity, through UserRepository); these sit on the same
// "api/IdentityUser" prefix as sibling minimal APIs. Routes + verbs + TypeAuth gates are byte-identical to the old
// controller actions. Composed into the dashboard by MapShiftIdentityDashboard().
internal static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        // GET api/IdentityUser/{key}/EffectivePermissions — Users Read (filter) AND-gated with AccessTrees Read (body).
        app.MapGet("api/IdentityUser/{key}/EffectivePermissions",
            async (string key, UserRepository userRepo, ITypeAuthService typeAuth, IHashIdService hashIdService) =>
            {
                // AND-gate against AccessTrees Read (the RequireTypeAuthRead filter handles Users Read).
                if (!typeAuth.CanRead(ShiftIdentityActions.AccessTrees))
                    return Results.Json(new ShiftEntityResponse<UserEffectivePermissionsDTO>
                    {
                        Message = new Message("Forbidden", "Read access to both Users and Access Trees is required.")
                    }, statusCode: (int)HttpStatusCode.Forbidden);

                var userId = hashIdService.Decode<UserDTO>(key);

                var accessTree = await userRepo.GenerateEffectiveAccessTreeAsync(userId);

                return Results.Ok(new ShiftEntityResponse<UserEffectivePermissionsDTO>(new UserEffectivePermissionsDTO
                {
                    AccessTree = accessTree,
                }));
            })
            .RequireTypeAuthRead(ShiftIdentityActions.Users);

        // POST api/IdentityUser/AssignRandomPasswords — Users Write.
        app.MapPost("api/IdentityUser/AssignRandomPasswords",
            async (SelectStateDTO<UserListDTO> ids,
                   HttpContext httpContext,
                   UserRepository userRepo,
                   IMapper mapper,
                   ShiftIdentityConfiguration options,
                   [FromQuery(Name = "shareWithUser")] bool? shareWithUser,
                   [FromQuery(Name = "passwordLength")] int? passwordLength,
                   IEnumerable<ISendUserInfo>? sendUserInfos) =>
            {
                var users = userRepo.AssignRandomPasswords(await GetSelectedUsersAsync(httpContext, ids), passwordLength ?? 20, options.Security.RequirePasswordChange);

                var userInfos = mapper.Map<IEnumerable<UserInfoDTO>>(users);

                await userRepo.SaveChangesAsync();

                if (shareWithUser ?? false)
                {
                    if (sendUserInfos != null)
                        foreach (var sendUserInfo in sendUserInfos)
                            await sendUserInfo.SendUserInfoAsync(userInfos);
                }

                return Results.Ok(new ShiftEntityResponse<IEnumerable<UserInfoDTO>>
                {
                    Additional = new Dictionary<string, object>
                    {
                        ["Users"] = userInfos,
                    }
                });
            })
            .RequireTypeAuthWrite(ShiftIdentityActions.Users);

        // POST api/IdentityUser/ResetTotp — Users Write.
        app.MapPost("api/IdentityUser/ResetTotp",
            async (SelectStateDTO<UserListDTO> ids, HttpContext httpContext, UserRepository userRepo, IMapper mapper) =>
            {
                var users = await GetSelectedUsersAsync(httpContext, ids);
                foreach (var user in users)
                    await userRepo.SetTotpSecret(null, user);

                await userRepo.SaveChangesAsync();
                return Results.Ok(new ShiftEntityResponse<IEnumerable<UserInfoDTO>>(mapper.Map<IEnumerable<UserInfoDTO>>(users)));
            })
            .RequireTypeAuthWrite(ShiftIdentityActions.Users);

        // POST api/IdentityUser/VerifyEmails — Users Write. Generates a SAS-token verification link per user and
        // sends it via the registered ISendEmailVerification providers.
        app.MapPost("api/IdentityUser/VerifyEmails",
            async (SelectStateDTO<UserListDTO> ids,
                   HttpContext httpContext,
                   UserRepository userRepo,
                   IMapper mapper,
                   ShiftIdentityConfiguration options,
                   IHashIdService hashIdService,
                   LinkGenerator linkGenerator,
                   IEnumerable<ISendEmailVerification>? sendEmailVerifications) =>
            {
                var users = await GetSelectedUsersAsync(httpContext, ids);

                List<(User user, string fullUrl)> datas = new();

                foreach (var user in users)
                {
                    if (user.EmailVerified || string.IsNullOrWhiteSpace(user.Email))
                        continue;

                    var encodedId = hashIdService.Encode<UserDTO>(user.ID);

                    // Link to the (now minimal-API) VerifyEmail endpoint by name, so this SAS uniqueId matches the
                    // one UserManagerEndpoints.VerifyEmail reconstructs on validation (both use GetPathByName).
                    var url = linkGenerator.GetPathByName(httpContext, UserManagerEndpoints.VerifyEmailRouteName, new { userId = encodedId });
                    var uniqueId = $"{url}-{user.Email}";
                    var (token, expires) = TokenService.GenerateSASToken(uniqueId, encodedId,
                        DateTime.UtcNow.AddSeconds(options.SASToken.ExpiresInSeconds), options.SASToken.Key);

                    string baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
                    var fullUrl = $"{baseUrl}{(baseUrl.EndsWith('/') ? baseUrl.Substring(0, baseUrl.Length - 1) : "")}{url}?expires={expires}&token={token}";

                    user.VerificationSASToken = token;

                    datas.Add((user, fullUrl));
                }

                await userRepo.SaveChangesAsync();

                foreach (var data in datas)
                    if (sendEmailVerifications is not null)
                        foreach (var sendEmailVerification in sendEmailVerifications)
                            await sendEmailVerification.SendEmailVerificationAsync(data.fullUrl, mapper.Map<UserDataDTO>(data.user));

                return Results.Ok(new ShiftEntityResponse<IEnumerable<UserInfoDTO>>(mapper.Map<IEnumerable<UserInfoDTO>>(users)));
            })
            .RequireTypeAuthWrite(ShiftIdentityActions.Users);

        // POST api/IdentityUser/VerifyPhones — Users Write.
        app.MapPost("api/IdentityUser/VerifyPhones",
            async (SelectStateDTO<UserListDTO> ids, HttpContext httpContext, UserRepository userRepo, IMapper mapper) =>
            {
                var users = userRepo.VerifyPhonesAsync(await GetSelectedUsersAsync(httpContext, ids));

                var userInfos = mapper.Map<IEnumerable<UserListDTO>>(users);

                await userRepo.SaveChangesAsync();

                return Results.Ok(new ShiftEntityResponse<IEnumerable<UserListDTO>> { Entity = userInfos });
            })
            .RequireTypeAuthWrite(ShiftIdentityActions.Users);

        // POST api/IdentityUser/ImportUsers — Users Write + per-row Branch data-level check.
        app.MapPost("api/IdentityUser/ImportUsers",
            async (UserImportDTO userImport,
                   HttpContext httpContext,
                   UserRepository userRepo,
                   ITypeAuthService typeAuth,
                   IEnumerable<ISendUserInfo>? sendUserInfos) =>
            {
                try
                {
                    var selfBranchId = httpContext.GetHashedCompanyBranchID()!;
                    foreach (var user in userImport.Users)
                        if (!typeAuth.CanRead(ShiftIdentityActions.DataLevelAccess.Branches, user.CompanyBranchID, selfBranchId))
                            throw new ShiftEntityException(new Message("Error", "Unauthorized"), (int)HttpStatusCode.Forbidden);

                    var users = await userRepo.UserImportAsync(userImport.Users);

                    await userRepo.SaveChangesAsync();

                    if (userImport.SendLoginInfoByEmail)
                        if (sendUserInfos is not null)
                            foreach (var sendUserInfo in sendUserInfos)
                                await sendUserInfo.SendUserInfoAsync(users.Select(x => new UserInfoDTO
                                {
                                    Username = x.Username,
                                    PlainTextPassword = x.Password,
                                    Email = x.Email,
                                }));
                }
                catch (ShiftEntityException ex)
                {
                    return Results.Json(new ShiftEntityResponse<UserImportDTO>
                    {
                        Message = ex.Message,
                        Additional = ex.AdditionalData,
                    }, statusCode: ex.HttpStatusCode);
                }

                return Results.Ok(new ShiftEntityResponse<UserImportDTO> { Entity = userImport });
            })
            .RequireTypeAuthWrite(ShiftIdentityActions.Users);

        return app;
    }

    // Selected-entity resolution ported from ShiftEntitySecureControllerAsync.GetSelectedEntitiesAsync — the CRUD
    // handler is new()-able and resolves the repository (same request scope, so same tracked DbContext as the
    // injected UserRepository).
    private static Task<List<User>> GetSelectedUsersAsync(HttpContext httpContext, SelectStateDTO<UserListDTO> ids)
        => new ShiftEntityCrudHandler<UserRepository, User, UserListDTO, UserDTO>().GetSelectedEntitiesAsync(httpContext, ids);
}
