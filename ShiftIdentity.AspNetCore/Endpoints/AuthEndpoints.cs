using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services.Interfaces;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authorization;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Auth;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Core.Models;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Web;

namespace Microsoft.AspNetCore.Builder;

// The Auth endpoints (login / refresh / MFA / auth-code / external-token), ported from the API AuthController
// (routes + verbs byte-identical). These are thin wrappers over the UNCHANGED AuthService / AuthCodeService — the
// class-level [Authorize] + per-action [AllowAnonymous]/[StepUp] map to .AllowAnonymous()/.RequireAuthorization(policy)
// here. Backed by the AuthEndpointTests safety net. Host calls MapShiftIdentityAuthEndpoints() where the controller
// used to be mapped by MapControllers().
public static class ShiftIdentityAuthEndpoints
{
    public static IEndpointRouteBuilder MapShiftIdentityAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // POST api/Auth/Login — anonymous.
        app.MapPost("api/Auth/Login", async (LoginDTO loginDto, AuthService authService) =>
        {
            var result = await authService.LoginAsync(loginDto);

            if (result.Result != LoginResultEnum.Success)
                return Results.BadRequest(new ShiftEntityResponse<TokenDTO> { Message = new Message { Body = result.ErrorMessage } });

            return Results.Ok(new ShiftEntityResponse<TokenDTO>(result.Token));
        }).AllowAnonymous();

        // POST api/Auth/Refresh — anonymous; validates the refresh token manually. no-store cache headers preserved.
        app.MapPost("api/Auth/Refresh", async (RefreshDTO dto, HttpContext httpContext, AuthService authService, ShiftIdentityLocalizer Loc) =>
        {
            httpContext.Response.Headers["Cache-Control"] = "no-store, no-cache";
            httpContext.Response.Headers["Pragma"] = "no-cache";
            httpContext.Response.Headers["Expires"] = "0";

            var token = await authService.RefreshAsync(dto.RefreshToken);

            if (token is null)
                return Results.BadRequest(new ShiftEntityResponse<TokenDTO> { Message = new Message { Body = Loc["Invalid refresh token"] } });

            return Results.Ok(new ShiftEntityResponse<TokenDTO>(token));
        }).AllowAnonymous();

        // POST api/Auth/Login/mfa — gated by the step-up MFA policy (a full access token must NOT satisfy it).
        app.MapPost("api/Auth/Login/mfa", async (MfaDTO mfaDto, HttpContext httpContext, AuthService authService, ShiftIdentityLocalizer Loc) =>
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Results.BadRequest(new ShiftEntityResponse<TokenDTO> { Message = new Message { Body = Loc["Invalid token"] } });

            var tokenDto = await authService.MfaLogin(userId, mfaDto.Code);

            if (tokenDto is null)
                return Results.BadRequest(new ShiftEntityResponse<TokenDTO> { Message = new Message { Body = Loc["Invalid code"] } });

            return Results.Ok(new ShiftEntityResponse<TokenDTO>(tokenDto));
        }).RequireAuthorization(StepUpPolicy.For(AuthPurpose.Mfa, allowAccessToken: false));

        // POST api/Auth/AuthCode — anonymous route, but requires an authenticated user (unless fake identity).
        app.MapPost("api/Auth/AuthCode", async (GenerateAuthCodeDTO generateAuthCodeDto, HttpContext httpContext, AuthCodeService authCodeService, IClaimService claimService, ShiftIdentityConfiguration shiftIdentityConfiguration, ShiftIdentityLocalizer Loc) =>
        {
            if (!shiftIdentityConfiguration.IsFakeIdentity && !httpContext.User!.Identity!.IsAuthenticated)
                return Results.Unauthorized();

            var loginUser = claimService.GetUser();

            var authCode = await authCodeService.GenerateCodeAsync(generateAuthCodeDto, loginUser.ID.ToLong());

            if (authCode is null)
                return Results.BadRequest(new ShiftEntityResponse<AuthCodeModel> { Message = new Message { Body = Loc["Failed to genearate auth-code"] } });

            var authCodeDto = new AuthCodeModel
            {
                AppId = authCode.AppId,
                AppDisplayName = authCode.AppDisplayName,
                Code = authCode.Code,
                ReturnUrl = generateAuthCodeDto.ReturnUrl,
                RedirectUri = authCode.RedirectUri
            };

            return Results.Ok(new ShiftEntityResponse<AuthCodeModel>(authCodeDto));
        }).AllowAnonymous();

        // POST api/Auth/TokenWithAppIdOnly — anonymous; PKCE-verified external token.
        app.MapPost("api/Auth/TokenWithAppIdOnly", async (GenerateExternalTokenWithAppIdOnlyDTO dto, AuthService authService, ShiftIdentityLocalizer Loc) =>
        {
            var token = await authService.GenrerateExternalTokenWithAppIdOnly(dto);

            if (token is null)
                return Results.BadRequest(new ShiftEntityResponse<TokenDTO> { Message = new Message { Body = Loc["Failed to genearate token"] } });

            return Results.Ok(new ShiftEntityResponse<TokenDTO>(token));
        }).AllowAnonymous();

        // GET Auth/AuthCode — anonymous server-side redirect, ported from the MVC AuthController (route + verb
        // byte-identical; a pure redirect, no view). Real identity: bounce back to ReturnUrl/base. Fake identity:
        // fetch an auth-code from api/Auth/AuthCode and redirect to the app's RedirectUri carrying the code. The
        // DTO binds from the query string ([AsParameters] mirrors the controller's [FromQuery]).
        app.MapGet("Auth/AuthCode",
            async ([AsParameters] GenerateAuthCodeDTO generateAuthCodeDto, HttpContext httpContext, ShiftIdentityConfiguration shiftIdentityConfiguration) =>
            {
                string GetBaseUri()
                {
                    var b = new UriBuilder(httpContext.Request.Scheme, httpContext.Request.Host.Host, httpContext.Request.Host.Port ?? -1);
                    if (b.Uri.IsDefaultPort)
                        b.Port = -1;
                    return b.Uri.AbsoluteUri;
                }

                if (!shiftIdentityConfiguration.IsFakeIdentity)
                    return Results.Redirect(generateAuthCodeDto.ReturnUrl ?? GetBaseUri());

                var http = new HttpClient();

                using var response = await http.PostAsJsonAsync(GetBaseUri() + "Api/Auth/AuthCode", generateAuthCodeDto);

                if (!response.IsSuccessStatusCode)
                    return Results.Redirect(generateAuthCodeDto.ReturnUrl ?? GetBaseUri());

                var result = await response.Content.ReadFromJsonAsync<ShiftEntityResponse<AuthCodeModel>>();

                var uriBuilder = new UriBuilder(result!.Entity!.RedirectUri);
                var query = HttpUtility.ParseQueryString(uriBuilder.Query);
                query["AuthCode"] = result.Entity.Code.ToString();
                query["ReturnUrl"] = result.Entity.ReturnUrl;
                uriBuilder.Query = query.ToString();

                return Results.Redirect(uriBuilder.ToString());
            }).AllowAnonymous();

        return app;
    }
}
