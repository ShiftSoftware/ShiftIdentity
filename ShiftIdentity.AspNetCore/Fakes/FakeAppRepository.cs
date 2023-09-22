using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.DTOs.App;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.IRepositories;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Fakes;

public class FakeAppRepository : IAppRepository
{
    private readonly ShiftIdentityOptions shiftIdentityOptions;
    public FakeAppRepository(ShiftIdentityOptions shiftIdentityOptions)
    {
        this.shiftIdentityOptions = shiftIdentityOptions;
    }
    public async Task<App?> GetAppAsync(string appId)
    {
        return new App
        {
            AppId = shiftIdentityOptions.App.AppId,
            RedirectUri = shiftIdentityOptions.App.RedirectUri,
            DisplayName = shiftIdentityOptions.App.DisplayName
        };
    }
}
