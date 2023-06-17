using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Auth;
using ShiftSoftware.ShiftIdentity.Core.Models;
using System.Net.Http.Json;
using System.Reflection;
using System.Web;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Controllers.MVC
{

    [Route("[controller]")]
    [Controller]
    [AllowAnonymous]
    public class AuthController : ControllerBase
    {
        ShiftIdentityConfiguration shiftIdentityConfiguration;
        public AuthController(ShiftIdentityConfiguration shiftIdentityConfiguration)
        {
            this.shiftIdentityConfiguration = shiftIdentityConfiguration;
        }

        [HttpGet("AuthCode")]
        public async Task<RedirectResult> GenerageAuthCode([FromQuery] GenerateAuthCodeDTO generateAuthCodeDto)
        {
            if (!this.shiftIdentityConfiguration.IsFakeIdentity)
            {
                return Redirect(generateAuthCodeDto.ReturnUrl ?? GetBaseUri());
            }

            var http = new HttpClient();

            using var response = await http.PostAsJsonAsync(GetBaseUri() + "Api/Auth/AuthCode", generateAuthCodeDto);

            if (!response.IsSuccessStatusCode)
                return Redirect(generateAuthCodeDto.ReturnUrl ?? GetBaseUri());

            var result = await response.Content.ReadFromJsonAsync<ShiftEntityResponse<AuthCodeModel>>();

            var uriBuilder = new UriBuilder(result.Entity.RedirectUri);
            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            query["AuthCode"] = result.Entity.Code.ToString();
            query["ReturnUrl"] = result.Entity.ReturnUrl;
            uriBuilder.Query = query.ToString();
            var longurl = uriBuilder.ToString();

            return Redirect(longurl);
        }

        string GetBaseUri()
        {
            var uriBuilder = new UriBuilder(this.Request.Scheme, this.Request.Host.Host, this.Request.Host.Port ?? -1);
            if (uriBuilder.Uri.IsDefaultPort)
            {
                uriBuilder.Port = -1;
            }

            return uriBuilder.Uri.AbsoluteUri;
        }
    }
}
