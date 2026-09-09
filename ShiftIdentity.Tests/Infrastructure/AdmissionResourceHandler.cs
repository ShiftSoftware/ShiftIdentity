using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

namespace ShiftIdentity.Tests.Infrastructure;

// Test-only resource server uses the same strict v2 codec as sensitive admission.
internal sealed class AdmissionResourceHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, IdentityAdmissionServices services)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return Task.FromResult(AuthenticateResult.NoResult());
        var token = header[7..];
        if (new AdmissionTokenCodec(services.Options, services.Clock).ValidateAccess(token, services.Client) is null)
            return Task.FromResult(AuthenticateResult.Fail("Invalid session."));
        var user = new ClaimsPrincipal(new ClaimsIdentity(new JsonWebToken(token).Claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(user, Scheme.Name)));
    }
}
