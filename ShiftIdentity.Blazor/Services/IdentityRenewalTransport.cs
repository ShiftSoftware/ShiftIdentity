using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Enums;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

internal sealed record IdentityRenewalResult(TokenDTO? Token = null, bool Remove = false, bool KeepCurrentAccess = false);

internal abstract class IdentityRenewalTransport
{
    internal virtual bool NotifyChanges => false;
    internal virtual TokenDTO? Readable(TokenDTO? token) => token;
    internal virtual void ValidateForStorage(TokenDTO token) { }
    internal abstract Task<IdentityRenewalResult> RenewAsync(string refreshToken);
}

internal sealed class LegacyIdentityRenewalTransport(TokenRefreshService service) : IdentityRenewalTransport
{
    internal override async Task<IdentityRenewalResult> RenewAsync(string refreshToken) =>
        new(await service.RefreshTokenAsync(refreshToken));
}

internal sealed class AdmissionIdentityRenewalTransport(HttpClient http) : IdentityRenewalTransport
{
    internal override bool NotifyChanges => true;
    internal override TokenDTO? Readable(TokenDTO? token) => IsSession(token) ? token : null;
    internal override void ValidateForStorage(TokenDTO token)
    {
        if (!IsSession(token)) throw new ArgumentException("Only a current ordinary v2 session can be stored.", nameof(token));
    }

    internal override async Task<IdentityRenewalResult> RenewAsync(string refreshToken)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/identity/v2/refresh", new RenewSessionRequest(refreshToken));
            var result = await response.Content.ReadFromJsonAsync<AuthOutcome>();
            if (response.IsSuccessStatusCode && result is SessionIssued session && IsSession(session.Session))
                return new(session.Session);
            return new(Remove: result is AuthenticationRefused { Code: not AuthenticationFailure.Unavailable }, KeepCurrentAccess: true);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or TaskCanceledException)
        { return new(KeepCurrentAccess: true); }
    }

    // Structural validation only. The API verifies signatures, client context and current authority.
    private static bool IsSession(TokenDTO? value)
    {
        if (value is null || value.Flow != AuthPurpose.None || string.IsNullOrWhiteSpace(value.RefreshToken) ||
            value.TokenLifeTimeInSeconds is not (> 0 and <= 900)) return false;
        try
        {
            var token = new JsonWebToken(value.Token);
            return token.GetClaim("shift_schema").Value == "2" && token.GetClaim("shift_purpose").Value == "access" &&
                token.ValidTo > DateTime.UtcNow;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return false; }
    }
}
