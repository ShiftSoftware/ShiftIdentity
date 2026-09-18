using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using ShiftSoftware.ShiftIdentity.Data.Authentication;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>
/// The deployed client carries a bearer JWT, not a v2 handle/verifier pair. This signed envelope carries that
/// pair to the same SQL operation checks. It is never an access token, refresh token or independent proof.
/// It only wraps newly admitted login steps; a pre-cutover temporary credential is read by
/// <see cref="LegacyTemporaryTokenCodec"/> and finished by its own bridge instead.
/// </summary>
internal sealed class LegacyLoginTokenCodec(IdentityAdmissionServices services)
{
    private const string Handle = "shift_login_handle", Verifier = "shift_login_verifier", Operation = "shift_login_operation";
    private string Audience => "shift-login-step:" + services.Client.ID;
    private SymmetricSecurityKey Key => new(HMACSHA256.HashData(services.Options.OperationKey,
        Encoding.UTF8.GetBytes("ShiftIdentity.LegacyLoginStep.v1")));

    internal sealed record Credential(ClaimsPrincipal Principal, string Handle, string Verifier,
        AuthenticationOperationPurpose Purpose, AuthPurpose Flow, DateTimeOffset ExpiresAt);

    internal TokenDTO Issue(IdentitySecurityTransaction unit, AuthenticationChallenge challenge, string verifier)
    {
        var flow = Flow(challenge.Step);
        var now = services.Clock.GetUtcNow();
        if (flow == AuthPurpose.None || challenge.Handle is null || challenge.ExpiresAt <= now)
            throw new IdentitySecurityConflictException("Login step is no longer available.");
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, services.HashIds.Encode<UserDTO>(unit.User.ID)),
            new Claim(ClaimTypes.Name, unit.User.Username), new Claim(ClaimTypes.GivenName, unit.User.FullName),
            new Claim(ShiftIdentityClaims.TokenPurpose, flow.ToString()),
            new Claim(Handle, challenge.Handle), new Claim(Verifier, verifier),
            new Claim(Operation, ((int)challenge.Purpose).ToString(CultureInfo.InvariantCulture))
        };
        var jwt = new JwtSecurityToken(services.Options.Issuer, Audience, claims, now.UtcDateTime,
            challenge.ExpiresAt.UtcDateTime, new SigningCredentials(Key, SecurityAlgorithms.HmacSha256));
        return new()
        {
            Token = new JwtSecurityTokenHandler().WriteToken(jwt), Flow = flow,
            TokenLifeTimeInSeconds = Math.Max(0, challenge.ExpiresAt.ToUnixTimeSeconds() - now.ToUnixTimeSeconds()),
            UserData = new() { ID = unit.User.ID.ToString(CultureInfo.InvariantCulture),
                Username = unit.User.Username, FullName = unit.User.FullName }
        };
    }

    internal Credential? Read(string? authorization)
    {
        try
        {
            if (authorization is null || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
            var token = authorization[7..];
            if (token.Length > 8192) return null;
            var parts = token.Split('.');
            if (parts.Length != 3) return null;
            foreach (var part in parts.Take(2))
            {
                using var json = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(part));
                if (json.RootElement.ValueKind != JsonValueKind.Object) return null;
                var names = json.RootElement.EnumerateObject().Select(x => x.Name).ToArray();
                if (names.Distinct(StringComparer.Ordinal).Count() != names.Length) return null;
            }
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 8192 };
            var principal = handler.ValidateToken(token, new()
            {
                ValidIssuer = services.Options.Issuer, ValidAudience = Audience, IssuerSigningKey = Key,
                ValidateIssuerSigningKey = true, ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.Zero,
                LifetimeValidator = (nbf, exp, _, _) => nbf is not null && exp is not null &&
                    nbf <= services.Clock.GetUtcNow().UtcDateTime && exp > services.Clock.GetUtcNow().UtcDateTime
            }, out var validated);
            string? One(string name) => principal.FindAll(name).Select(x => x.Value).ToArray() is [var value] ? value : null;
            if (!Enum.TryParse<AuthPurpose>(One(ShiftIdentityClaims.TokenPurpose), out var flow) || flow == AuthPurpose.None ||
                !int.TryParse(One(Operation), out var purposeValue) ||
                One(Handle) is not { } handle || One(Verifier) is not { Length: 43 } verifier ||
                One(ClaimTypes.NameIdentifier) is null ||
                !OperationCredential.TryRead(handle, services.Options.OperationKey, out _, out _) ||
                !Allowed((AuthenticationOperationPurpose)purposeValue, flow)) return null;
            return new(principal, handle, verifier, (AuthenticationOperationPurpose)purposeValue, flow,
                new DateTimeOffset(validated.ValidTo, TimeSpan.Zero));
        }
        catch (Exception e) when (e is SecurityTokenException or ArgumentException or FormatException or JsonException or OverflowException)
        { return null; }
    }

    internal static AuthPurpose Flow(AuthenticationStep step) => step switch
    {
        AuthenticationStep.ExistingMfa => AuthPurpose.Mfa,
        AuthenticationStep.NewMfa => AuthPurpose.MfaEnrollment,
        AuthenticationStep.PasswordChange => AuthPurpose.ChangePassword,
        _ => AuthPurpose.None
    };

    private static bool Allowed(AuthenticationOperationPurpose purpose, AuthPurpose flow) => purpose switch
    {
        AuthenticationOperationPurpose.Login => flow == AuthPurpose.Mfa,
        AuthenticationOperationPurpose.PasswordChange => flow is AuthPurpose.ChangePassword or AuthPurpose.Mfa or AuthPurpose.MfaEnrollment,
        AuthenticationOperationPurpose.MfaEnrollment => flow == AuthPurpose.MfaEnrollment,
        _ => false
    };
}
