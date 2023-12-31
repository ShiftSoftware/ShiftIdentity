﻿using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Blazor.Providers;

public interface IShiftIdentityProvider
{
    public Task<HttpResponse<ShiftEntityResponse<TokenDTO?>?>> GetTokenWithAppIdOnlyAsync(string baseUrl, GenerateExternalTokenWithAppIdOnlyDTO dto);
    public Task<HttpResponse<ShiftEntityResponse<TokenDTO?>?>> RefreshTokenAsync(string baseUrl, RefreshDTO dto);
    public Task<HttpResponse<ShiftEntityResponse<UserDataDTO?>?>> GetUserDataAsync(string baseUrl);
    public Task<HttpResponse<ShiftEntityResponse<UserDataDTO?>?>> UpdateUserDataAsync(string baseUrl, UserDataDTO dto);
    public Task<HttpResponse<ShiftEntityResponse<TokenDTO?>?>> LoginAsync(string baseUrl, LoginDTO dto);
}
