using ShiftSoftware.ShiftIdentity.Core.Authentication;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public sealed partial class AuthenticationFlow
{
    // Administrator account mutations. Responses never contain a session and never touch the browser's stored credentials.
    public Task<AuthOutcome> SetAccountPasswordAsync(string access, long userID, string password, bool requireChangeAtNextLogin = true) =>
        SecurityRequestAsync("admin/password", new AdminSetPasswordRequest(userID, password, requireChangeAtNextLogin), Accepts(AdminAccountChange.Password), access);
    public Task<AuthOutcome> ChangeAccountUsernameAsync(string access, long userID, string username) =>
        SecurityRequestAsync("admin/username", new AdminUsernameChangeRequest(userID, username), Accepts(AdminAccountChange.Username), access);
    public Task<AuthOutcome> ChangeAccountEmailAsync(string access, long userID, string? email, bool sendVerification = true) =>
        SecurityRequestAsync("admin/email", new AdminEmailChangeRequest(userID, email, sendVerification), Accepts(AdminAccountChange.Email), access);
    public Task<AuthOutcome> SetAccountActiveAsync(string access, long userID, bool active) =>
        SecurityRequestAsync("admin/status", new AdminAccountStatusRequest(userID, active), Accepts(AdminAccountChange.Active), access);
    /// <summary>Turns off another account's MFA where it is optional, with the note of the identity check behind the request.</summary>
    public Task<AuthOutcome> TurnOffAccountMfaAsync(string access, long userID, string verificationReference) =>
        SecurityRequestAsync("admin/mfa/turn-off", new AdminMfaTurnOffRequest(userID, verificationReference), Accepts(AdminAccountChange.Mfa), access);
    /// <summary>The same for a dashboard form that holds the target's encoded key instead of its numeric ID.</summary>
    public Task<AuthOutcome> TurnOffAccountMfaAsync(string access, string userKey, string verificationReference) =>
        SecurityRequestAsync("admin/mfa/turn-off", new AdminMfaTurnOffRequest(0, verificationReference, userKey), Accepts(AdminAccountChange.Mfa), access);

    private static Func<AuthOutcome, bool> Accepts(AdminAccountChange change) =>
        result => result is AdminAccountChanged changed && changed.Change == change;
}
