using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.AspNetCore.Extensions;
using ShiftSoftware.ShiftIdentity.Core.Models;
using System.Net.Http.Json;
using System.Web;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Controllers.MVC;

[Route("[controller]")]
[Controller]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    [HttpGet("AuthCode")]
    public async Task<RedirectResult> GenerageAuthCode([FromQuery] GenerateAuthCodeDTO generateAuthCodeDto)
    {
        var http= new HttpClient();

        using var response = await http.PostAsJsonAsync(this.GetBaseUri() + "Api/Auth/AuthCode", generateAuthCodeDto);

        if (!response.IsSuccessStatusCode)
            return Redirect(generateAuthCodeDto.ReturnUrl?? this.GetBaseUri());
        
        var result = await response.Content.ReadFromJsonAsync<ShiftEntityResponse<AuthCodeDTO>>();

        var uriBuilder = new UriBuilder(result.Entity.RedirectUri);
        var query = HttpUtility.ParseQueryString(uriBuilder.Query);
        query["AuthCode"] = result.Entity.Code.ToString();
        query["ReturnUrl"] = result.Entity.ReturnUrl;
        uriBuilder.Query = query.ToString();
        var longurl = uriBuilder.ToString();

        return Redirect(longurl);
    }
}
