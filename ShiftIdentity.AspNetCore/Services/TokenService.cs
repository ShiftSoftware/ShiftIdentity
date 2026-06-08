using Microsoft.IdentityModel.Tokens;
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

    /// <summary>
    /// Short-lived token issued at login when the user must change their password first. Carries
    /// only enough to identify the user to <c>CompletePasswordChange</c> — no refresh token, no
    /// <c>AccessTree</c>, no role claims, and <see cref="Filters.RequirePasswordChangeFilter"/>
    /// makes other endpoints reject it.
    /// </summary>
    public TokenDTO GenerateChallengeToken(User user)
    {
        const int challengeLifetimeSeconds = 600; // 10 minutes — enough to fill the form, not enough to linger.

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, ShiftEntityHashIdService.Encode<Core.DTOs.User.UserDTO>(user.ID)),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.GivenName, user.FullName),
            new Claim(ShiftIdentityClaims.RequirePasswordChange, true.ToString()),
        };

        var rsa = RSA.Create();
        rsa.ImportRSAPrivateKey(Convert.FromBase64String(shiftIdentityConfiguration.Token.RSAPrivateKeyBase64), out _);
        var signingCredentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256);

        var jwt = new JwtSecurityToken(
            issuer: shiftIdentityConfiguration.Token.Issuer,
            claims: claims,
            expires: DateTime.UtcNow.AddSeconds(challengeLifetimeSeconds),
            signingCredentials: signingCredentials);

        return new TokenDTO
        {
            Token = new JwtSecurityTokenHandler().WriteToken(jwt),
            TokenLifeTimeInSeconds = challengeLifetimeSeconds,
            RefreshToken = string.Empty,
            RefreshTokenLifeTimeInSeconds = 0,
            RequirePasswordChange = true,
            UserData = new TokenUserDataDTO
            {
                FullName = user.FullName,
                ID = user.ID.ToString(),
                Username = user.Username,
                CompanyType = user.Company?.CompanyType,
            }
        };
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

    public TokenDTO GenerateToken(User user)
    {
        // Safety net: never mint a full session token for a user who still owes a password change.
        if (shiftIdentityConfiguration.Security.RequirePasswordChange && user.RequireChangePassword)
            throw new InvalidOperationException("Cannot issue a session token while RequireChangePassword is set.");

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

        if (user.IsSuperAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "superadmin"));

        if (user.Email != null)
            claims.Add(new Claim(ClaimTypes.Email, user.Email));

        if (user.Phone != null)
            claims.Add(new Claim(ClaimTypes.MobilePhone, user.Phone));

        if (!string.IsNullOrWhiteSpace(user.AccessTree))
            claims.Add(new Claim(TypeAuthClaimTypes.AccessTree, user.AccessTree));

        if (user.AccessTrees?.Count() > 0)
            foreach (var accessTree in user.AccessTrees)
                claims.Add(new Claim(TypeAuthClaimTypes.AccessTree, accessTree.AccessTree.Tree));

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
            RefreshToken = GenerateRefreshToken(user),
            RefreshTokenLifeTimeInSeconds = shiftIdentityConfiguration.RefreshToken.ExpireSeconds,
            RequirePasswordChange = false,
            UserData = new TokenUserDataDTO
            {
                FullName = user.FullName,
                ID = user.ID.ToString(),
                Username = user.Username,
                CompanyType = user.Company?.CompanyType,
                UserSignature = string.IsNullOrWhiteSpace(user.Signature) ? null : JsonSerializer.Deserialize<IEnumerable<ShiftFileDTO>>(user.Signature)
            }
        };

        if (user.Email is not null)
            result.UserData.Emails = [ new EmailDTO { Email = user.Email }];

        if (user.Phone is not null)
            result.UserData.Phones = [ new PhoneDTO { Phone = user.Phone }];

        return result;
    }

    private string GenerateRefreshToken(User user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.ID.ToString()),
            // Versions this refresh token. AuthService.RefreshAsync rejects the refresh once the
            // user's SecurityStamp rolls (password change / log-out-everywhere), so a stolen or
            // lingering refresh token cannot outlive a credential change.
            new Claim(ShiftIdentityClaims.SecurityStamp, user.SecurityStamp.ToString())
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
