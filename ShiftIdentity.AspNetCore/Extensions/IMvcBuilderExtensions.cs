using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.ShiftIdentity.AspNetCore;
using ShiftSoftware.ShiftIdentity.AspNetCore.Extensions;
using ShiftSoftware.ShiftIdentity.AspNetCore.Fakes;
using ShiftSoftware.ShiftIdentity.AspNetCore.Models;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services.Interfaces;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.App;
using ShiftSoftware.ShiftIdentity.Core.IRepositories;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftwareLocalization.Identity;
using System.Security.Cryptography;


namespace Microsoft.Extensions.DependencyInjection;

public static class IMvcBuilderExtensions
{
    public static IMvcBuilder AddShiftIdentity(this IMvcBuilder builder, string tokenIssuer, string tokenRSAPublicKeyBase64,
        Type? localizationResource = null, bool setAsDefaultScheme = true)
    {
        builder.Services.AddShiftIdentity(tokenIssuer, tokenRSAPublicKeyBase64, localizationResource, setAsDefaultScheme);
        return builder;
    }

    public static IMvcBuilder AddFakeIdentityEndPoints(this IMvcBuilder builder, TokenSettingsModel tokenConfiguration, TokenUserDataDTO userData, AppDTO app, string? userPassword, params string[] accessTrees)
    {
        builder.Services.AddFakeIdentityEndPoints(tokenConfiguration, userData, app, userPassword, accessTrees);
        return builder;
    }
}
