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
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.AspNetCore.Endpoints;

namespace Microsoft.AspNetCore.Builder;

// The Auth endpoints (login / refresh / MFA / auth-code / external-token), ported from the API AuthController
// (routes + verbs byte-identical). Login, app-code and refresh adapters use staged admission when registered; the
// class-level [Authorize] + per-action [AllowAnonymous]/[StepUp] map to .AllowAnonymous()/.RequireAuthorization(policy)
// here. Backed by the AuthEndpointTests safety net. Host calls MapShiftIdentityAuthEndpoints() where the controller
// used to be mapped by MapControllers().
public static class ShiftIdentityAuthEndpoints
{
    private static IResult CompatibleTokenResult(AuthOutcome outcome, string message) => outcome is SessionIssued issued
        ? Results.Ok(new ShiftEntityResponse<TokenDTO>(issued.Session))
        : Results.Json(new ShiftEntityResponse<TokenDTO> { Message = new Message { Body = message } },
            statusCode: outcome is AuthenticationRefused { Code: AuthenticationFailure.Unavailable } ? 503 : 400);

    public static IEndpointRouteBuilder MapShiftIdentityAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // POST api/Auth/Login — anonymous.
        app.MapPost("api/Auth/Login", async (LoginDTO loginDto, AuthService authService, HttpContext httpContext, ShiftIdentityConfiguration configuration) =>
        {
            if (httpContext.RequestServices.GetService<IdentityAdmissionServices>() is { } admission)
            {
                httpContext.Response.Headers["Cache-Control"] = "no-store";
                var outcome = await AuthService.BeginCompatibleLoginAsync(admission, loginDto, configuration, httpContext.RequestAborted);
                var compatible = authService.CompatibleLoginResult(outcome);
                return LegacyLoginEndpoints.TokenResult(outcome, compatible.ErrorMessage ?? "Login could not be completed.");
            }
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

            if (httpContext.RequestServices.GetService<IdentityAdmissionServices>() is { } admission)
                return CompatibleTokenResult(await AuthService.RenewCompatibleSessionAsync(admission,
                    new(dto.RefreshToken), httpContext.RequestAborted), Loc["Invalid refresh token"]);

            var token = await authService.RefreshAsync(dto.RefreshToken);

            if (token is null)
                return Results.BadRequest(new ShiftEntityResponse<TokenDTO> { Message = new Message { Body = Loc["Invalid refresh token"] } });

            return Results.Ok(new ShiftEntityResponse<TokenDTO>(token));
        }).AllowAnonymous();

        // POST api/Auth/Login/mfa — gated by the step-up MFA policy (a full access token must NOT satisfy it).
        app.MapPost("api/Auth/Login/mfa", async (MfaDTO mfaDto, HttpContext httpContext, AuthService authService, ShiftIdentityLocalizer Loc) =>
        {
            if (LegacyLoginEndpoints.IsStaged(httpContext))
                return await LegacyLoginEndpoints.CompleteMfaAsync(httpContext, mfaDto.Code);
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Results.BadRequest(new ShiftEntityResponse<TokenDTO> { Message = new Message { Body = Loc["Invalid token"] } });

            var tokenDto = await authService.MfaLogin(userId, mfaDto.Code);

            if (tokenDto is null)
                return Results.BadRequest(new ShiftEntityResponse<TokenDTO> { Message = new Message { Body = Loc["Invalid code"] } });

            return Results.Ok(new ShiftEntityResponse<TokenDTO>(tokenDto));
        }).RequireAuthorization(StepUpPolicy.For(AuthPurpose.Mfa, allowAccessToken: false));

        // POST api/Auth/AuthCode — anonymous route, but requires an authenticated user.
        app.MapPost("api/Auth/AuthCode", async (GenerateAuthCodeDTO generateAuthCodeDto, HttpContext httpContext, AuthCodeService authCodeService, IClaimService claimService, ShiftIdentityLocalizer Loc) =>
        {
            if (httpContext.RequestServices.GetService<IdentityAdmissionServices>() is { } admission)
            {
                httpContext.Response.Headers["Cache-Control"] = "no-store";
                if (AdmissionRules.ReadSignedIn(admission, httpContext.Request.Headers.Authorization) is null)
                    return Results.Unauthorized();
                var outcome = await AuthService.CreateAppCodeAsync(admission, httpContext.Request.Headers.Authorization,
                    generateAuthCodeDto, httpContext.RequestAborted);
                if (outcome is AppCodeIssued issued) return Results.Ok(new ShiftEntityResponse<AuthCodeModel>(issued.Code));
                return Results.Json(new ShiftEntityResponse<AuthCodeModel>
                    { Message = new Message { Body = Loc["Failed to genearate auth-code"] } },
                    statusCode: outcome is AuthenticationRefused { Code: AuthenticationFailure.Unavailable } ? 503 : 400);
            }
            if (!httpContext.User!.Identity!.IsAuthenticated)
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
        app.MapPost("api/Auth/TokenWithAppIdOnly", async (GenerateExternalTokenWithAppIdOnlyDTO dto, HttpContext httpContext, AuthService authService, ShiftIdentityLocalizer Loc) =>
        {
            if (httpContext.RequestServices.GetService<IdentityAdmissionServices>() is { } admission)
            {
                httpContext.Response.Headers["Cache-Control"] = "no-store";
                return CompatibleTokenResult(await AuthService.ExchangeAppCodeAsync(admission, dto, httpContext.RequestAborted),
                    Loc["Failed to genearate token"]);
            }
            var token = await authService.GenrerateExternalTokenWithAppIdOnly(dto);

            if (token is null)
                return Results.BadRequest(new ShiftEntityResponse<TokenDTO> { Message = new Message { Body = Loc["Failed to genearate token"] } });

            return Results.Ok(new ShiftEntityResponse<TokenDTO>(token));
        }).AllowAnonymous();

        // GET Auth/AuthCode — anonymous server-side redirect, ported from the MVC AuthController (route + verb
        // byte-identical; a pure redirect, no view): bounce back to ReturnUrl, or the host's base URL. The DTO binds
        // from the query string ([AsParameters] mirrors the controller's [FromQuery]).
        app.MapGet("Auth/AuthCode",
            ([AsParameters] GenerateAuthCodeDTO generateAuthCodeDto, HttpContext httpContext) =>
            {
                if (generateAuthCodeDto.ReturnUrl is not null)
                    return Results.Redirect(generateAuthCodeDto.ReturnUrl);

                var b = new UriBuilder(httpContext.Request.Scheme, httpContext.Request.Host.Host, httpContext.Request.Host.Port ?? -1);
                if (b.Uri.IsDefaultPort)
                    b.Port = -1;
                return Results.Redirect(b.Uri.AbsoluteUri);
            }).AllowAnonymous();

        return app;
    }
}
