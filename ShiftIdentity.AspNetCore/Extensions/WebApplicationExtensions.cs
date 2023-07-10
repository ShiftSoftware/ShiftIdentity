using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Auth;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Models;
using System.Net.Http.Json;
using System.Web;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Extensions
{
    public static class WebApplicationExtensions
    {
        public static WebApplication AddFakeIdentityEndPoints(this WebApplication app)
        {
            var shiftIdentityOptions = app.Services.GetRequiredService<ShiftIdentityOptions>();
            var shiftEntityOptions = app.Services.GetRequiredService<ShiftEntityODataOptions>();

            var odataPrefix = shiftEntityOptions.RoutePrefix;

            app.MapGet($"{odataPrefix}/{Core.Constants.IdentityRoutePreifix}PublicUser", () =>
            {
                return new Dictionary<string, object> {
                    { "@odata.count", 1 },
                    { "Value", new List<PublicUserListDTO> {
                        new PublicUserListDTO { ID = shiftIdentityOptions.UserData.ID.ToString(), Name = shiftIdentityOptions.UserData.FullName }
                    } }
                };
            });

            app.MapGet($"{Core.Constants.IdentityRoutePreifix}/Auth/AuthCode", async (ShiftIdentityConfiguration shiftIdentityConfiguration, HttpRequest request) =>
            {
                var generateAuthCodeDto = new GenerateAuthCodeDTO
                {
                    AppId = request!.Query![nameof(GenerateAuthCodeDTO.AppId)]!,
                    CodeChallenge = request!.Query![nameof(GenerateAuthCodeDTO.CodeChallenge)]!,
                    ReturnUrl = request!.Query![nameof(GenerateAuthCodeDTO.ReturnUrl)]!
                };

                var requestUriBuilder = new UriBuilder(request.Scheme, request.Host.Host, request.Host.Port ?? -1);

                if (requestUriBuilder.Uri.IsDefaultPort)
                {
                    requestUriBuilder.Port = -1;
                }

                var baseUrl = requestUriBuilder.Uri.AbsoluteUri;

                if (!shiftIdentityConfiguration.IsFakeIdentity)
                {
                    return Microsoft.AspNetCore.Http.Results.Redirect(generateAuthCodeDto.ReturnUrl ?? baseUrl);
                }

                var http = new HttpClient();

                using var response = await http.PostAsJsonAsync(baseUrl + "Api/Auth/AuthCode", generateAuthCodeDto);

                if (!response.IsSuccessStatusCode)
                    return Microsoft.AspNetCore.Http.Results.Redirect(generateAuthCodeDto.ReturnUrl ?? baseUrl);

                var result = await response.Content.ReadFromJsonAsync<ShiftEntityResponse<AuthCodeModel>>();

                var uriBuilder = new UriBuilder(result!.Entity!.RedirectUri);
                var query = HttpUtility.ParseQueryString(uriBuilder.Query);
                query["AuthCode"] = result.Entity.Code.ToString();
                query["ReturnUrl"] = result.Entity.ReturnUrl;
                uriBuilder.Query = query.ToString();
                var longurl = uriBuilder.ToString();

                return Microsoft.AspNetCore.Http.Results.Redirect(longurl);
            });

            return app;
        }
    }
}
