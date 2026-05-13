using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services;

public class UserManagerService
{
    private const string BASE_URL = "usermanager/";
    private readonly HttpService http;

    public UserManagerService(HttpService http)
    {
        this.http = http;
    }

    public async Task<HttpResponse<ShiftEntityResponse<UserDataDTO>>> GetUserDataAsync()
    {
        return await http.GetAsync<ShiftEntityResponse<UserDataDTO>>(BASE_URL + "userdata");
    }
}