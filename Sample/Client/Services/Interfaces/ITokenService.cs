using Sample.Client.General;
using ShiftSoftware.ShiftIdentity.Model;

namespace Sample.Client.Services.Interfaces
{
    public interface ITokenService
    {
        IHttpService HttpService { get; }

        Task<HttpResponseModel<TokenDTO>> GetTokenAsync(GenerateExternalTokenWithAppIdOnlyDTO dto);
    }
}