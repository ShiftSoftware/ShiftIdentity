using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Helpers;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Models;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Services;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Endpoints;

internal static class CookieAuthEndpoints
{
    internal static async Task<IResult> Login(
        HttpContext httpContext,
        ShiftSoftware.ShiftIdentity.AspNetCore.Services.AuthService authService,
        LoginDTO? loginDto)
    {
        if (loginDto == null)
            return Results.BadRequest(new ShiftEntityResponse<TokenDTO>
            {
                Message = new Message { Body = "Request body is required." }
            });

        var result = await authService.LoginAsync(loginDto);

        if (result.Result != ShiftSoftware.ShiftIdentity.AspNetCore.Models.LoginResultEnum.Success)
        {
            return Results.BadRequest(new ShiftEntityResponse<TokenDTO>
            {
                Message = new Message { Body = result.ErrorMessage }
            });
        }

        await CookieAuthHelpers.SignInWithToken(httpContext, result.Token);

        return Results.Ok(new
        {
            result.Token.UserData,
            result.Token.RequirePasswordChange,
        });
    }

    internal static async Task<IResult> SignInWithToken(
        HttpContext httpContext,
        ShiftIdentityCookieAuthOptions cookieAuthOptions,
        SignInWithTokenRequest? request)
    {
        if (request == null)
            return Results.BadRequest(new ShiftEntityResponse<TokenDTO>
            {
                Message = new Message { Body = "Request body is required." }
            });

        var issuer = cookieAuthOptions.JwtIssuer;
        var publicKeyBase64 = cookieAuthOptions.JwtPublicKeyBase64;

        if (string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(publicKeyBase64))
            return Results.Json(new ShiftEntityResponse<TokenDTO>
            {
                Message = new Message { Body = "Token validation is not configured." }
            }, statusCode: StatusCodes.Status500InternalServerError);

        var rsa = RSA.Create();
        rsa.ImportRSAPublicKey(Convert.FromBase64String(publicKeyBase64), out _);
        var rsaSecurityKey = new RsaSecurityKey(rsa);

        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = rsaSecurityKey,
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = false,
            ValidateLifetime = true,
        };

        try
        {
            tokenHandler.ValidateToken(request.Token, validationParameters, out _);
        }
        catch (SecurityTokenException)
        {
            return Results.Json(new ShiftEntityResponse<TokenDTO>
            {
                Message = new Message { Body = "Invalid token." }
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var token = new TokenDTO
        {
            Token = request.Token,
            RefreshToken = request.RefreshToken,
            TokenLifeTimeInSeconds = request.TokenLifeTimeInSeconds,
        };

        await CookieAuthHelpers.SignInWithToken(httpContext, token);
        return Results.Ok();
    }

    internal static async Task<IResult> Refresh(HttpContext httpContext, ICookieAuthManager authManager)
    {
        var authResult = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var refreshToken = authResult.Properties?.GetString("refresh_token");

        if (refreshToken == null)
            return Results.Json(new ShiftEntityResponse<TokenDTO>
            {
                Message = new Message { Body = "Invalid refresh token" }
            }, statusCode: StatusCodes.Status401Unauthorized);

        var newToken = await authManager.RefreshAsync(refreshToken);
        if (newToken == null)
            return Results.Json(new ShiftEntityResponse<TokenDTO>
            {
                Message = new Message { Body = "Invalid refresh token" }
            }, statusCode: StatusCodes.Status401Unauthorized);

        await CookieAuthHelpers.SignInWithToken(httpContext, newToken);

        return Results.Ok(new { newToken.UserData });
    }

    internal static async Task<IResult> Logout(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Ok();
    }
}
