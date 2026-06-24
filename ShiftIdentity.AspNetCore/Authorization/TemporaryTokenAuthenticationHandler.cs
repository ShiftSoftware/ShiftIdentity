using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authorization;

/// <summary>
/// Authenticates ShiftIdentity <em>temporary</em> tokens presented as a <c>Bearer</c> token.
/// These are the short-lived, purpose-bound tokens issued before login completes for an enforced
/// flow (forced change-password / two-factor enrollment). They are signed with the refresh-token
/// key, so the default access-token bearer scheme rejects them — this scheme validates them instead.
/// <para>
/// The resulting principal carries the same <see cref="ClaimTypes.NameIdentifier"/> and
/// <c>TokenPurpose</c> claims as the original token, so step-up authorization can check the purpose
/// and endpoints can read the user id uniformly regardless of how the request was authenticated.
/// </para>
/// </summary>
public class TemporaryTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private const string BearerPrefix = "Bearer ";

    private readonly TokenService tokenService;

    public TemporaryTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        TokenService tokenService)
        : base(options, logger, encoder)
    {
        this.tokenService = tokenService;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();

        // Not our concern — let other schemes (e.g. the access-token bearer scheme) try.
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var token = header[BearerPrefix.Length..].Trim();

        if (string.IsNullOrEmpty(token))
            return Task.FromResult(AuthenticateResult.NoResult());

        try
        {
            var principal = tokenService.ValidateTemporaryToken(token);
            if (principal is null)
                return Task.FromResult(AuthenticateResult.NoResult());

            // Re-wrap under this scheme so the identity is unambiguously authenticated and tagged.
            var identity = new ClaimsIdentity(principal.Claims, Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch
        {
            // Wrong key/issuer/expiry/algorithm — this token isn't a valid temporary token.
            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }
}
