using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.IRepositories;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.TypeAuth.Core;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

public class TokenService
{
    private readonly ShiftIdentityConfiguration shiftIdentityConfiguration;
    private readonly IUserRepository userRepository;

    public TokenService(ShiftIdentityConfiguration shiftIdentityConfiguration, IUserRepository userRepository)
    {
        this.shiftIdentityConfiguration = shiftIdentityConfiguration;
        this.userRepository = userRepository;
    }

    public async Task<TokenDTO> GenerateInternalJwtTokenAsync(User user)
    {
        return GenerateToken(user);
    }

    public TokenDTO? GenerateExternalJwtToken(User user, AuthCodeModel authCode)
    {
        if (shiftIdentityConfiguration.Security.RequirePasswordChange && user.RequireChangePassword)
            return null;

        return GenerateToken(user, true);
    }

    public async Task<TokenDTO?> RefreshAsync(string refreshToken)
    {
        try
        {
            var claimPrincipal = GetPrincipalFromRefreshToken(refreshToken);

            if (claimPrincipal is null)
                return null;

            var userId = long.Parse(claimPrincipal?.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await userRepository.FindAsync(userId);

            if (user is null)
                return null;

            //Check if user is stil active
            if (!user.IsActive)
                return null;

            var token = GenerateToken(user);

            return token;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private ClaimsPrincipal? GetPrincipalFromRefreshToken(string token)
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

    private TokenDTO GenerateToken(User user, bool external = false)
    {
        var requirePasswordChange = shiftIdentityConfiguration.Security.RequirePasswordChange && user.RequireChangePassword;

        var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, ShiftEntityHashIdService.Encode<Core.DTOs.User.UserDTO>(user.ID)),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.GivenName, user.FullName),
            };

        claims.Add(new Claim(ShiftEntity.Core.Constants.RegionIdClaim, ShiftEntityHashIdService.Encode<RegionDTO>(user.RegionID)));

        claims.Add(new Claim(ShiftEntity.Core.Constants.CompanyIdClaim, ShiftEntityHashIdService.Encode<CompanyDTO>(user.CompanyID)));

        claims.Add(new Claim(ShiftEntity.Core.Constants.CompanyBranchIdClaim, ShiftEntityHashIdService.Encode<CompanyBranchDTO>(user.CompanyBranchID)));

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

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(shiftIdentityConfiguration.Token.Key));

        var token = new JwtSecurityToken(
            issuer: shiftIdentityConfiguration.Token.Issuer,
            claims: claims,
            expires: DateTime.UtcNow.AddSeconds(shiftIdentityConfiguration.Token.ExpireSeconds),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature));

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
            }
        };

        if (!requirePasswordChange)
        {
            if (user?.Email is not null)
                result.UserData.Emails = new List<EmailDTO> { new EmailDTO { Email = user.Email } };

            if (user?.Phone is not null)
                result.UserData.Phones = new List<PhoneDTO> { new PhoneDTO { Phone = user.Phone } };
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
