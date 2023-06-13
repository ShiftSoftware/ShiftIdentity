using Microsoft.AspNetCore.Http;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services.Interfaces;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using System.Security.Claims;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

public class ClaimService : IClaimService
{
    private readonly IHttpContextAccessor httpContextAccessor;

    public ClaimService(IHttpContextAccessor httpContextAccessor)
    {
        this.httpContextAccessor = httpContextAccessor;
    }

    public TokenUserDataDTO GetUser()
    {
        var dto = new TokenUserDataDTO
        {
            FullName = httpContextAccessor.HttpContext?.User?.Claims?.SingleOrDefault(c => c.Type == ClaimTypes.GivenName)?.Value!,
            Roles = httpContextAccessor.HttpContext?.User?.Claims.Where(c => c.Type == ClaimTypes.Role).Select(x => x.Value).ToList()!,
            ID = long.Parse(httpContextAccessor.HttpContext?.User?.Claims?.SingleOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value!),
            Username = httpContextAccessor.HttpContext?.User?.Claims?.SingleOrDefault(c => c.Type == ClaimTypes.Name)?.Value!
        };

        var email = httpContextAccessor.HttpContext?.User?.Claims?.SingleOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
        if (!string.IsNullOrWhiteSpace(email))
            dto.Emails = new List<EmailDTO> { new EmailDTO { Email = email } };

        var phone = httpContextAccessor.HttpContext?.User?.Claims?.SingleOrDefault(c => c.Type == ClaimTypes.MobilePhone)?.Value;
        if (!string.IsNullOrWhiteSpace(phone))
            dto.Phones = new List<PhoneDTO> { new PhoneDTO { Phone = phone } };

        return dto;
    }
}
