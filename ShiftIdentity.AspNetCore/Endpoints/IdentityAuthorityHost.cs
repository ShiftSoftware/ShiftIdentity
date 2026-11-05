using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Endpoints;

/// <summary>Tells the routes that exist with and without the authority which one this host runs.</summary>
public static class IdentityAuthorityHost
{
    public static bool IsEnabled(HttpContext http) => http.RequestServices.GetService<IdentityAdmissionServices>() is not null;
}
