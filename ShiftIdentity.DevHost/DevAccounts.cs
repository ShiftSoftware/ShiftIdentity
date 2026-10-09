using Microsoft.EntityFrameworkCore;
using ShiftIdentity.Tests.Infrastructure;

namespace ShiftIdentity.DevHost;

/// <summary>A seeded synthetic account. Every account has the fixture's password.</summary>
internal sealed record DevAccount(string Username, string Description, long UserID, bool Mfa);

internal static class DevAccounts
{
    public static async Task<IReadOnlyList<DevAccount>> SeedAsync(SqlIdentityFixture fixture)
    {
        var accounts = new List<DevAccount>
        {
            await CreateAsync(fixture, "dev-screen", "Allows device sign-in: type it on the phone page. Never asked for MFA.", deviceSignIn: true),
            await CreateAsync(fixture, "dev-plain", "Password only. Does not allow device sign-in, so the phone page refuses it."),
            await CreateAsync(fixture, "dev-mfa", "Password and an authenticator code. The /dev pages show the current code.", mfa: true),
            await CreateAsync(fixture, "dev-change", "Must change the password when signing in.", requireChange: true),
            await CreateAsync(fixture, "dev-deactivate", "Password only. Use it to try deactivation.")
        };
        return accounts;
    }

    private static async Task<DevAccount> CreateAsync(SqlIdentityFixture fixture, string username, string description,
        bool mfa = false, bool requireChange = false, bool deviceSignIn = false)
    {
        var id = await fixture.CreateSyntheticUserAsync(username, mfa: mfa, email: username + "@example.invalid");
        if (requireChange || deviceSignIn)
        {
            await using var db = fixture.CreateContext();
            var user = await db.Users.SingleAsync(x => x.ID == id);
            user.RequireChangePassword = requireChange;
            user.AllowDeviceSignIn = deviceSignIn;
            await db.SaveChangesAsync();
        }
        return new(username, description, id, mfa);
    }
}
