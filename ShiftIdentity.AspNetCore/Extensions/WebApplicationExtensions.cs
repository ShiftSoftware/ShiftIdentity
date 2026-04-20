using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.AspNetCore;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;

namespace Microsoft.AspNetCore.Builder;

public static class WebApplicationExtensions
{
    public static WebApplication AddFakeIdentityEndPoints(this WebApplication app)
    {
        var shiftIdentityOptions = app.Services.GetRequiredService<ShiftIdentityOptions>();

        app.MapGet($"api/{ShiftSoftware.ShiftIdentity.Core.Constants.IdentityRoutePreifix}PublicUser", () =>
        {
            return new Dictionary<string, object> {
                { "Count", 1 },
                { "Value", new List<PublicUserListDTO> {
                    new PublicUserListDTO { ID = shiftIdentityOptions.UserData.ID.ToString(), Name = shiftIdentityOptions.UserData.FullName }
                } }
            };
        });

        return app;
    }
}
