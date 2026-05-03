using Microsoft.AspNetCore.Http;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services.Interfaces;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using System.Security.Claims;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

public class ClaimService : IClaimService
{
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly IHashIdService hashIdService;

    public ClaimService(IHttpContextAccessor httpContextAccessor, IHashIdService hashIdService)
    {
        this.httpContextAccessor = httpContextAccessor;
        this.hashIdService = hashIdService;
    }

    public TokenUserDataDTO GetUser()
    {
        var dto = new TokenUserDataDTO
        {
            FullName = httpContextAccessor.HttpContext?.User?.Claims?.SingleOrDefault(c => c.Type == ClaimTypes.GivenName)?.Value!,
            ID = hashIdService.Decode<TokenUserDataDTO>(httpContextAccessor.HttpContext?.User?.Claims?.SingleOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value!).ToString(),
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
