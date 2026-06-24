using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

    public async Task<HttpResponse<ShiftEntityResponse<UserDataDTO>>> ChangePasswordAsync(ChangePasswordDTO dto)
    {
        return await http.PutAsync<ShiftEntityResponse<UserDataDTO>, ChangePasswordDTO>(BASE_URL + "ChangePassword", dto);
    }

    /// <summary>
    /// Starts a TOTP (authenticator app) enrollment by fetching a fresh otpauth URI and its QR code.
    /// </summary>
    public async Task<HttpResponse<ShiftEntityResponse<TotpDTO>>> StartTotpEnrollmentAsync()
    {
        return await http.GetAsync<ShiftEntityResponse<TotpDTO>>(BASE_URL + "StartTotpEnrollment");
    }

    /// <summary>
    /// Confirms a TOTP enrollment by validating a code from the user's authenticator app.
    /// </summary>
    public async Task<HttpResponse<ShiftEntityResponse<TokenDTO>>> ConfirmTotpAsync(string code, TotpDTO enrollment)
    {
        return await http.PostAsync<ShiftEntityResponse<TokenDTO>, TotpDTO>(
            BASE_URL + "ConfirmTotpEnrollment",
            new TotpDTO
            {
                Code = code,
                Secret = enrollment.Secret,
                SasToken = enrollment.SasToken,
                Expires = enrollment.Expires,
            });
    }
}