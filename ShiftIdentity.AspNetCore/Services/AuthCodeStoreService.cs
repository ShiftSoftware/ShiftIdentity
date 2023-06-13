using ShiftSoftware.ShiftIdentity.Core.Models;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;
public class AuthCodeStoreService
{
    private List<AuthCodeModel> authCodes = new List<AuthCodeModel>();

    public void RemoveExpireCodes()
    {
        authCodes.RemoveAll(x => DateTime.UtcNow > x.Expire);
    }

    public void RemoveCode(AuthCodeModel code)
    {
        authCodes.Remove(code);
    }

    public AuthCodeModel GetCode(Guid code)
    {
        return authCodes.SingleOrDefault(x => x.Code == code)!;
    }

    public void AddCode(AuthCodeModel code)
    {
        authCodes.Add(code);
    }

    public List<AuthCodeModel> GetCodes()
    {
        return authCodes;
    }
}
