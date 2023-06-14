using ShiftSoftware.ShiftIdentity.AspNetCore.Services.Interfaces;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Fakes;

public class FakeClaimService : IClaimService
{
    private readonly ShiftIdentityOptions shiftIdentityOptions;

    public FakeClaimService(ShiftIdentityOptions shiftIdentityOptions)
    {
        this.shiftIdentityOptions = shiftIdentityOptions;
    }

    public TokenUserDataDTO GetUser()
    {
        return this.shiftIdentityOptions.UserData;
    }
}
