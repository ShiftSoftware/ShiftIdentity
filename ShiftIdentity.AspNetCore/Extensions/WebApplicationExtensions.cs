using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Extensions
{
    public static class WebApplicationExtensions
    {
        public static WebApplication AddFakeIdentityEndPoints(this WebApplication app)
        {
            var shiftIdentityOptions = app.Services.GetRequiredService<ShiftIdentityOptions>();
            var shiftEntityOptions = app.Services.GetRequiredService<ShiftEntityOptions>();

            var odataPrefix = shiftEntityOptions.ODatat.RoutePrefix;

            app.MapGet($"{odataPrefix}/PublicUser", () =>
            {
                return new Dictionary<string, object> {
                    { "@odata.count", 1 },
                    { "Value", new List<PublicUserListDTO> {
                        new PublicUserListDTO { ID = shiftIdentityOptions.UserData.ID.ToString(), Name = shiftIdentityOptions.UserData.FullName }
                    } }
                };
            });

            return app;
        }
    }
}
