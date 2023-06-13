
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services.Interfaces
{
    public interface IClaimService
    {
        TokenUserDataDTO GetUser();
    }
}