using Microsoft.EntityFrameworkCore;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Data;

namespace ShiftIdentity.DevHost;

/// <summary>A seeded synthetic account. Every account has the fixture's password.</summary>
internal sealed record DevAccount(string Username, string Description, long UserID, bool Mfa);

internal static class DevAccounts
{
    /// <summary>
    /// The administrator's access: everything in the action trees this host registers, built the way a real host's
    /// built-in administrator is seeded. GeneralActionTree lets it load the dashboard's lists without a page size and
    /// with pages larger than five rows, which the user form needs.
    /// </summary>
    public static readonly Type[] ActionTrees = [typeof(ShiftIdentityActions), typeof(GeneralActionTree)];

    public static async Task<IReadOnlyList<DevAccount>> SeedAsync(SqlIdentityFixture fixture)
    {
        var accounts = new List<DevAccount>
        {
            await CreateAsync(fixture, "dev-admin", "Administrator with full access: the user form and the dashboard's lists. Built-in, so it cannot be edited.",
                accessTree: FullAccessTree.BuildJson(ActionTrees), builtIn: true),
            await CreateAsync(fixture, "dev-screen", "Allows device sign-in: type it on the phone page. Never asked for MFA.", deviceSignIn: true),
            await CreateAsync(fixture, "dev-plain", "Password only. Does not allow device sign-in, so the phone page refuses it."),
            await CreateAsync(fixture, "dev-mfa", "Password and an authenticator code. The /dev pages show the current code.", mfa: true),
            await CreateAsync(fixture, "dev-change", "Must change the password when signing in.", requireChange: true),
            await CreateAsync(fixture, "dev-deactivate", "Password only. Use it to try deactivation.")
        };
        return accounts;
    }

    private static async Task<DevAccount> CreateAsync(SqlIdentityFixture fixture, string username, string description,
        bool mfa = false, bool requireChange = false, bool deviceSignIn = false, string? accessTree = null, bool builtIn = false)
    {
        var id = await fixture.CreateSyntheticUserAsync(username, accessTree, mfa: mfa, email: username + "@example.invalid");
        if (requireChange || deviceSignIn || builtIn)
        {
            await using var db = fixture.CreateContext();
            var user = await db.Users.SingleAsync(x => x.ID == id);
            user.RequireChangePassword = requireChange;
            user.AllowDeviceSignIn = deviceSignIn;
            user.IsProtected = builtIn;
            await db.SaveChangesAsync();
        }
        return new(username, description, id, mfa);
    }
}
