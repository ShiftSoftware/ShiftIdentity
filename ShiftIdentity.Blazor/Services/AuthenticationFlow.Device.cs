using System.Net.Http.Json;
using System.Text.Json;
using ShiftSoftware.ShiftIdentity.Core.Authentication;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public sealed partial class AuthenticationFlow
{
    // Device sign-in, the phone's side: read the code a device shows, then sign the device in with the username and
    // password of the account it should use, or deny it. None of these requests carries, reads, replaces or removes the
    // browser's own session: the phone never signs in.

    /// <summary>
    /// Which device asks with this code, and the code's state. A read only, without a session. A code that matches
    /// nothing counts against this address's failed-code budget on the server.
    /// </summary>
    public async Task<AuthOutcome> ReadDeviceAuthorizationAsync(string userCode)
    {
        if (Busy) return new AuthenticationRefused(AuthenticationFailure.InvalidRequest);
        Busy = true;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/identity/v2/device/" + Uri.EscapeDataString(userCode));
            using var response = await http.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<AuthOutcome>();
            return result switch
            {
                AuthenticationRefused refused => refused,
                DeviceAuthorizationView when response.IsSuccessStatusCode => result,
                _ => new AuthenticationRefused(AuthenticationFailure.InvalidGrant)
            };
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException)
        { return new AuthenticationRefused(AuthenticationFailure.Unavailable); }
        finally { Busy = false; }
    }

    /// <summary>
    /// Signs the device that shows <paramref name="userCode"/> in as the account these credentials prove. A wrong
    /// password counts against that account, as at login. An account that owes a step answers with a challenge that
    /// cannot be continued here: it must sign in normally first.
    /// </summary>
    public Task<AuthOutcome> ApproveDeviceAuthorizationAsync(string userCode, string username, string password) => SecurityRequestAsync(
        "device/approve", new ApproveDeviceRequest(userCode, username, password),
        result => result is DeviceAuthorizationView { State: DeviceAuthorizationState.Approved } or ChallengeRequired);

    /// <summary>Refuses the device that shows <paramref name="userCode"/>. The device stops asking with that code.</summary>
    public Task<AuthOutcome> DenyDeviceAuthorizationAsync(string userCode) => SecurityRequestAsync(
        "device/deny", new DeviceUserCodeRequest(userCode),
        result => result is DeviceAuthorizationView { State: DeviceAuthorizationState.Denied });
}
