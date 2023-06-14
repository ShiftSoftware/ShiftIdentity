using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Core.Repositories;
using ShiftSoftware.TypeAuth.Core;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

public class TokenService
{
    private readonly ShiftIdentityOptions shiftIdentityOptions;
    private readonly IUserRepository userRepository;

    public TokenService(ShiftIdentityOptions configuration, IUserRepository userRepository)
    {
        this.shiftIdentityOptions = configuration;
        this.userRepository = userRepository;
    }

    public async Task<TokenDTO> GenerateInternalJwtTokenAsync(User user)
    {
        return GenerateToken(user);
    }

    public TokenDTO? GenerateExternalJwtToken(User user, AuthCodeModel authCode)
    {
        if (shiftIdentityOptions.Configuration.Security.RequirePasswordChange && user.RequireChangePassword)
            return null;

        return GenerateToken(user, true);
    }

    public async Task<TokenDTO?> RefreshAsync(string refreshToken)
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

    private ClaimsPrincipal? GetPrincipalFromRefreshToken(string token)
    {
        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false,
            ValidateIssuer = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = shiftIdentityOptions.Configuration.RefreshToken.Issuer,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(shiftIdentityOptions.Configuration.RefreshToken.Key)),
            ClockSkew = TimeSpan.Zero,
            LifetimeValidator = (notBefore, expires, securityToken,
validationParameters) =>
            {
                if (expires != null)
                    return expires > DateTime.UtcNow;

                return false;
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
        var requirePasswordChange = shiftIdentityOptions.Configuration.Security.RequirePasswordChange && user.RequireChangePassword;

        var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.ID.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.GivenName, user.FullName),
            };

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

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(shiftIdentityOptions.Configuration.Token.Key));

        var token = new JwtSecurityToken(
            issuer: shiftIdentityOptions.Configuration.Token.Issuer,
            claims: claims,
            expires: DateTime.UtcNow.AddSeconds(shiftIdentityOptions.Configuration.Token.ExpireSeconds),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature));

        string tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        var result = new TokenDTO
        {
            Token = tokenString,
            RefreshToken = GenerateRefreshToken(user.ID),
            RequirePasswordChange = requirePasswordChange,
            UserData = new TokenUserDataDTO
            {
                FullName = user.FullName,
                ID = user.ID,
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

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(shiftIdentityOptions.Configuration.RefreshToken.Key));

        var token = new JwtSecurityToken(
            issuer: shiftIdentityOptions.Configuration.RefreshToken.Issuer,
            audience: shiftIdentityOptions.Configuration.RefreshToken.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddSeconds(shiftIdentityOptions.Configuration.RefreshToken.ExpireSeconds),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature));

        string tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        return tokenString;
    }
}
