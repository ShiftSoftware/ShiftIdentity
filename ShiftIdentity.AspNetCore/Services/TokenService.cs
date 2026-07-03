using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.City;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Country;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Team;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.Enums;
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
    private readonly IHashIdService hashIdService;

    public TokenService(ShiftIdentityConfiguration shiftIdentityConfiguration, IHashIdService hashIdService)
    {
        this.shiftIdentityConfiguration = shiftIdentityConfiguration;
        this.hashIdService = hashIdService;
    }

    public TokenDTO GenerateInternalJwtToken(User user)
    {
        return GenerateToken(user);
    }

    public TokenDTO? GenerateExternalJwtToken(User user, AuthCodeModel authCode)
    {
        if (user.RequireChangePassword)
            return null;

        return GenerateToken(user, true);
    }

    public TokenDTO IssueLoginToken(User user, bool mfaSatisfiedThisSession = false)
    {
        if (user.RequireChangePassword)
            return GenerateChangePasswordToken(user);

        if (!mfaSatisfiedThisSession)
        {
            var mfa = shiftIdentityConfiguration.MfaSettings;
            if (mfa.Enabled)
            {
                if (user.TotpSecret is null && mfa.Mandatory)
                    return GenerateMfaEnrollmentToken(user);
                if (user.TotpSecret is not null)
                    return GenerateMfaToken(user);
            }
        }

        return GenerateInternalJwtToken(user);
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
        var userId = hashIdService.Encode<Core.DTOs.User.UserDTO>(user.ID);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.GivenName, user.FullName),
            new Claim(ShiftEntity.Core.Constants.RegionIdClaim, hashIdService.Encode<RegionDTO>(user.RegionID!.Value)),
            new Claim(ShiftEntity.Core.Constants.CompanyIdClaim, hashIdService.Encode<CompanyDTO>(user.CompanyID!.Value)),
            new Claim(ShiftEntity.Core.Constants.CompanyBranchIdClaim, hashIdService.Encode<CompanyBranchDTO>(user.CompanyBranchID!.Value)),
            new Claim(ShiftEntity.Core.Constants.CompanyTypeClaim, user.Company?.CompanyType.ToString()??string.Empty),
        };

        if (user.CountryID is not null)
            claims.Add(new Claim(ShiftEntity.Core.Constants.CountryIdClaim, hashIdService.Encode<CountryDTO>(user.CountryID!.Value)));

        if (user.CompanyBranch is not null)
            claims.Add(new Claim(ShiftEntity.Core.Constants.CityIdClaim, hashIdService.Encode<CityDTO>(user.CompanyBranch.CityID!.Value)));

        foreach (var team in user.TeamUsers)
            claims.Add(new Claim(ShiftEntity.Core.Constants.TeamIdsClaim, hashIdService.Encode<TeamDTO>(team.TeamID)));

        claims.Add(new Claim(ShiftIdentityClaims.ExternalToken, external.ToString().ToLower()));

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
            RefreshToken = GenerateRefreshToken(userId),
            RefreshTokenLifeTimeInSeconds = shiftIdentityConfiguration.RefreshToken.ExpireSeconds,
            UserData = new TokenUserDataDTO
            {
                FullName = user.FullName,
                ID = user.ID.ToString(),
                Username = user.Username,
                CompanyType = user.Company?.CompanyType,
            }
        };

        if (user?.Email is not null)
            result.UserData.Emails = new List<EmailDTO> { new EmailDTO { Email = user.Email } };

        if (user?.Phone is not null)
            result.UserData.Phones = new List<PhoneDTO> { new PhoneDTO { Phone = user.Phone } };

        result.UserData.UserSignature = string.IsNullOrWhiteSpace(user?.Signature) ? null : JsonSerializer.Deserialize<IEnumerable<ShiftFileDTO>>(user!.Signature);

        return result;
    }

    private string GenerateRefreshToken(string userId)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
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

    public TokenDTO GenerateMfaToken(User user)
    {
        return GenerateTemporaryToken(user, AuthPurpose.Mfa);
    }

    public TokenDTO GenerateMfaEnrollmentToken(User user)
    {
        return GenerateTemporaryToken(user, AuthPurpose.MfaEnrollment);
    }

    public TokenDTO GenerateChangePasswordToken(User user)
    {
        return GenerateTemporaryToken(user, AuthPurpose.ChangePassword);
    }

    /// <summary>
    /// Resolves the settings used to sign and validate temporary (pre-login) tokens — the change-password,
    /// MFA, and MFA-enrollment flows. These tokens are HMAC-SHA512 signed exactly like refresh tokens, so
    /// when a consumer upgrades without configuring
    /// <see cref="ShiftIdentityConfiguration.TemporaryTokenSettings"/> we fall back to the (already mandatory)
    /// refresh-token settings instead of throwing a <see cref="NullReferenceException"/>. Both generation and
    /// validation call this, so the signing key/issuer can never drift between the two.
    /// </summary>
    private TemporaryTokenSettingsModel GetTemporaryTokenSettings()
    {
        var temp = shiftIdentityConfiguration.TemporaryTokenSettings;
        var refresh = shiftIdentityConfiguration.RefreshToken;
        var token = shiftIdentityConfiguration.Token;

        var key = !string.IsNullOrWhiteSpace(temp?.Key) ? temp!.Key : refresh?.Key;

        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                $"ShiftIdentity: cannot issue temporary tokens because neither " +
                $"{nameof(ShiftIdentityConfiguration.TemporaryTokenSettings)}.{nameof(TemporaryTokenSettingsModel.Key)} nor " +
                $"{nameof(ShiftIdentityConfiguration.RefreshToken)}.{nameof(RefreshTokenSettingsModel.Key)} is configured.");

        return new TemporaryTokenSettingsModel
        {
            Key = key,
            Issuer = temp?.Issuer ?? refresh?.Issuer ?? token?.Issuer ?? string.Empty,
            Audience = temp?.Audience ?? refresh?.Audience ?? token?.Audience ?? string.Empty,
            ExpireSeconds = temp is not null && temp.ExpireSeconds > 0 ? temp.ExpireSeconds : 300,
        };
    }

    private TokenDTO GenerateTemporaryToken(User user, AuthPurpose purpose)
    {
        var userId = hashIdService.Encode<Core.DTOs.User.UserDTO>(user.ID);
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.GivenName, user.FullName),
            new Claim(ShiftIdentityClaims.TokenPurpose, purpose.ToString()),
        };

        var settings = GetTemporaryTokenSettings();
        var expire = settings.ExpireSeconds;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key));

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddSeconds(expire),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature));

        string tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        var result = new TokenDTO
        {
            Token = tokenString,
            TokenLifeTimeInSeconds = expire,
            Flow = purpose,
            UserData = new TokenUserDataDTO
            {
                FullName = user.FullName,
                ID = user.ID.ToString(),
                Username = user.Username,
            }
        };

        return result;
    }

    public ClaimsPrincipal? ValidateTemporaryToken(string token)
    {
        var settings = GetTemporaryTokenSettings();

        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false,
            ValidateIssuer = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = settings.Issuer,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),
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
        var jwtSecurityToken = securityToken as JwtSecurityToken;

        if (jwtSecurityToken == null || !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha512Signature, StringComparison.InvariantCultureIgnoreCase))
            throw new SecurityTokenInvalidAlgorithmException("Token validation failed: unexpected signing algorithm.");

        return principal;
    }
}
