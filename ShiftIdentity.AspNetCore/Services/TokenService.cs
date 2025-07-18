﻿using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.City;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Country;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Team;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.TypeAuth.Core;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

public class TokenService
{
    private readonly ShiftIdentityConfiguration shiftIdentityConfiguration;

    public TokenService(ShiftIdentityConfiguration shiftIdentityConfiguration)
    {
        this.shiftIdentityConfiguration = shiftIdentityConfiguration;
    }

    public TokenDTO GenerateInternalJwtToken(User user)
    {
        return GenerateToken(user);
    }

    public TokenDTO? GenerateExternalJwtToken(User user, AuthCodeModel authCode)
    {
        if (shiftIdentityConfiguration.Security.RequirePasswordChange && user.RequireChangePassword)
            return null;

        return GenerateToken(user, true);
    }

    public ClaimsPrincipal? GetPrincipalFromRefreshToken(string token)
    {
        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = shiftIdentityConfiguration.RefreshToken.Audience,
            ValidateIssuer = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = shiftIdentityConfiguration.RefreshToken.Issuer,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(shiftIdentityConfiguration.RefreshToken.Key)),
            ClockSkew = TimeSpan.Zero,
            LifetimeValidator = (DateTime? notBefore, DateTime? expires, SecurityToken securityToken,
                                     TokenValidationParameters validationParameters) =>
            {
                var now = DateTime.UtcNow;

                if (notBefore != null && now < notBefore)
                    return false;

                if (expires != null)
                    return expires > now;

                return true;
            }
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken securityToken);
        JwtSecurityToken jwtSecurityToken = securityToken as JwtSecurityToken;

        if (jwtSecurityToken == null || !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha512Signature, StringComparison.InvariantCultureIgnoreCase))
            return null;

        return principal;
    }

    public TokenDTO GenerateToken(User user, bool external = false)
    {
        var requirePasswordChange = shiftIdentityConfiguration.Security.RequirePasswordChange && user.RequireChangePassword;

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, ShiftEntityHashIdService.Encode<Core.DTOs.User.UserDTO>(user.ID)),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.GivenName, user.FullName),
            new Claim(ShiftEntity.Core.Constants.RegionIdClaim, ShiftEntityHashIdService.Encode<RegionDTO>(user.RegionID!.Value)),
            new Claim(ShiftEntity.Core.Constants.CompanyIdClaim, ShiftEntityHashIdService.Encode<CompanyDTO>(user.CompanyID!.Value)),
            new Claim(ShiftEntity.Core.Constants.CompanyBranchIdClaim, ShiftEntityHashIdService.Encode<CompanyBranchDTO>(user.CompanyBranchID!.Value)),
            new Claim(ShiftEntity.Core.Constants.CompanyTypeClaim, user.Company?.CompanyType.ToString()??string.Empty),
        };

        if (user.CountryID is not null)
            claims.Add(new Claim(ShiftEntity.Core.Constants.CountryIdClaim, ShiftEntityHashIdService.Encode<CountryDTO>(user.CountryID!.Value)));

        if (user.CompanyBranch is not null)
            claims.Add(new Claim(ShiftEntity.Core.Constants.CityIdClaim, ShiftEntityHashIdService.Encode<CityDTO>(user.CompanyBranch.CityID!.Value)));

        foreach (var team in user.TeamUsers)
            claims.Add(new Claim(ShiftEntity.Core.Constants.TeamIdsClaim, ShiftEntityHashIdService.Encode<TeamDTO>(team.TeamID)));

        claims.Add(new Claim(ShiftIdentityClaims.ExternalToken, external.ToString().ToLower()));

        if (requirePasswordChange)
        {
            claims.Add(new Claim(ShiftIdentityClaims.RequirePasswordChange, true.ToString()));
        }
        else
        {
            if (user.IsSuperAdmin)
                claims.Add(new Claim(ClaimTypes.Role, "superadmin"));

            if (user.Email != null)
                claims.Add(new Claim(ClaimTypes.Email, user.Email));

            if (user.Phone != null)
                claims.Add(new Claim(ClaimTypes.MobilePhone, user.Phone));

            //Store access tree per user
            if (!string.IsNullOrWhiteSpace(user.AccessTree))
                claims.Add(new Claim(TypeAuthClaimTypes.AccessTree, user.AccessTree));

            //Store access-trees in calim
            if (user.AccessTrees?.Count() > 0)
                foreach (var accessTree in user.AccessTrees)
                    claims.Add(new Claim(TypeAuthClaimTypes.AccessTree, accessTree.AccessTree.Tree));
        }

        var rsa = RSA.Create();
        rsa.ImportRSAPrivateKey(Convert.FromBase64String(shiftIdentityConfiguration.Token.RSAPrivateKeyBase64), out _);
        var signingCredentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: shiftIdentityConfiguration.Token.Issuer,
            claims: claims,
            expires: DateTime.UtcNow.AddSeconds(shiftIdentityConfiguration.Token.ExpireSeconds),
            signingCredentials: signingCredentials);

        string tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        var result = new TokenDTO
        {
            Token = tokenString,
            TokenLifeTimeInSeconds = shiftIdentityConfiguration.Token.ExpireSeconds,
            RefreshToken = GenerateRefreshToken(user.ID),
            RefreshTokenLifeTimeInSeconds = shiftIdentityConfiguration.RefreshToken.ExpireSeconds,
            RequirePasswordChange = requirePasswordChange,
            UserData = new TokenUserDataDTO
            {
                FullName = user.FullName,
                ID = user.ID.ToString(),
                Username = user.Username,
                CompanyType = user.Company?.CompanyType,
            }
        };

        if (!requirePasswordChange)
        {
            if (user?.Email is not null)
                result.UserData.Emails = new List<EmailDTO> { new EmailDTO { Email = user.Email } };

            if (user?.Phone is not null)
                result.UserData.Phones = new List<PhoneDTO> { new PhoneDTO { Phone = user.Phone } };

            result.UserData.UserSignature = string.IsNullOrWhiteSpace(user?.Signature) ? null : JsonSerializer.Deserialize<IEnumerable<ShiftFileDTO>>(user!.Signature);
        }

        return result;
    }

    private string GenerateRefreshToken(long userId)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(shiftIdentityConfiguration.RefreshToken.Key));

        var token = new JwtSecurityToken(
            issuer: shiftIdentityConfiguration.RefreshToken.Issuer,
            audience: shiftIdentityConfiguration.RefreshToken.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddSeconds(shiftIdentityConfiguration.RefreshToken.ExpireSeconds),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature));

        string tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        return tokenString;
    }
}
