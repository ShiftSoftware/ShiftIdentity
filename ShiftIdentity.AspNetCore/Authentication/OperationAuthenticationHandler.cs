using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShiftSoftware.ShiftIdentity.Core.Authentication;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

internal sealed class OperationAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IdentityAdmissionServices admission) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string SchemeName = "IdentityOperation";
    internal const string PurposeClaim = "identity_operation_purpose";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Operation ", StringComparison.Ordinal)) return AuthenticateResult.NoResult();
        if (!OperationCredential.TryRead(header[10..], admission.Options.OperationKey, out var id, out var digest))
            return AuthenticateResult.Fail("Invalid operation.");
        try
        {
            var op = await admission.Store.ReadOperationAsync(id, Context.RequestAborted);
            if (op is null || !CryptographicOperations.FixedTimeEquals(op.HandleDigest, digest) ||
                op.ClientID != admission.Client.ID || op.Audience != admission.Client.Audience ||
                op.External != admission.Client.External) return AuthenticateResult.Fail("Invalid operation.");
            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, op.UserID.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim(PurposeClaim, op.Purpose.ToString())
            ], SchemeName);
            return AuthenticateResult.Success(new(new ClaimsPrincipal(identity), SchemeName));
        }
        catch (Data.Authentication.IdentitySecurityUnavailableException)
        {
            Context.Items[SchemeName + ".Unavailable"] = true;
            return AuthenticateResult.Fail("Operation store unavailable.");
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var unavailable = Context.Items.ContainsKey(SchemeName + ".Unavailable");
        Response.Headers.CacheControl = "no-store";
        Response.StatusCode = unavailable ? 503 : 401;
        return Response.WriteAsJsonAsync<AuthOutcome>(new AuthenticationRefused(unavailable
            ? AuthenticationFailure.Unavailable : AuthenticationFailure.InvalidGrant));
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 403;
        Response.Headers.CacheControl = "no-store";
        return Response.WriteAsJsonAsync<AuthOutcome>(new AuthenticationRefused(AuthenticationFailure.InvalidGrant));
    }
}
