using Bunit;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.User;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// The production User form groups related fields into titled sections, so the password and email choices sit with
/// the fields they qualify instead of somewhere down one long column.
/// </summary>
[Trait("Category", "Ui"), Collection("User form")]
public sealed class UserFormLayoutTests
{
    private static readonly string[] SectionOrder = ["user-section-profile", "user-section-security", "user-section-contacts", "user-section-permissions"];

    [Fact]
    public async Task A_new_user_form_groups_fields_into_profile_security_contacts_and_permissions()
    {
        await using var context = UserFormHarness.Create(out var transport);
        var cut = context.Render<UserForm>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=user-section-permissions]")));

        Assert.Equal(SectionOrder, cut.FindAll("[data-testid^=user-section-]").Select(x => x.GetAttribute("data-testid")));

        var profile = cut.Find("[data-testid=user-section-profile]");
        foreach (var label in new[] { "Full Name", "Company Branch", "Birth Date", "Integration Id" })
            Assert.Contains(label, profile.TextContent);

        // The password, its generator and its next-login choice are one cluster inside Security, with the username and status.
        var security = cut.Find("[data-testid=user-section-security]");
        Assert.NotNull(security.QuerySelector("[data-testid=user-password] input[type=password]"));
        Assert.Contains(security.QuerySelectorAll("button"), b => b.TextContent.Contains("Generate password"));
        Assert.Contains("Username", security.TextContent);
        Assert.Contains("Active", security.TextContent);
        Assert.DoesNotContain("Authenticator App (TOTP)", security.TextContent);
        // Entering a password adds a full-width notice at the bottom of Security that carries the next-login choice.
        cut.Find("[data-testid=user-password] input").Change("Synthetic password 7");
        var passwordNotice = cut.Find("[data-testid=user-section-security] [data-testid=user-password-require-change]");
        Assert.Contains("mud-alert-text-warning", passwordNotice.ClassName);
        Assert.Contains("A new password will be saved for this user.", passwordNotice.TextContent);
        Assert.NotNull(passwordNotice.QuerySelector("input[type=checkbox]"));
        Assert.Equal("user-password-require-change", cut.Find("[data-testid=user-section-security] .mud-grid > .mud-grid-item:last-child .mud-alert").GetAttribute("data-testid"));

        // The email and its verification choice sit together inside Contacts, next to the phone.
        var contacts = cut.Find("[data-testid=user-section-contacts]");
        Assert.NotNull(contacts.QuerySelector("[data-testid=user-email] input"));
        Assert.Contains("Email", contacts.TextContent);
        Assert.Contains("Phone", contacts.TextContent);
        // A new address adds the same kind of notice at the bottom of Contacts with the verification choice.
        cut.Find("[data-testid=user-email] input").Change("new@example.invalid");
        var emailNotice = cut.Find("[data-testid=user-section-contacts] [data-testid=user-email-send-verification]");
        Assert.Contains("mud-alert-text-warning", emailNotice.ClassName);
        Assert.Contains("This address will be saved unverified.", emailNotice.TextContent);
        Assert.NotNull(emailNotice.QuerySelector("input[type=checkbox]"));
        Assert.Equal("user-email-send-verification", cut.Find("[data-testid=user-section-contacts] .mud-grid > .mud-grid-item:last-child .mud-alert").GetAttribute("data-testid"));

        var permissions = cut.Find("[data-testid=user-section-permissions]");
        Assert.Contains("Access Trees", permissions.TextContent);
        Assert.Contains("Actions", permissions.TextContent);
        Assert.Empty(cut.FindAll("[data-testid=user-effective-permissions]"));
        Assert.Empty(transport.Posts);
    }

    [Fact]
    public async Task An_existing_user_shows_authenticator_status_in_security_and_effective_permissions_in_the_permissions_header()
    {
        await using var context = UserFormHarness.Create(out var transport);
        transport.Existing = new UserDTO
        {
            ID = "42", Username = "synthetic-existing", FullName = "Synthetic Existing", IsActive = true, TotpEnabled = true,
            Email = "existing@example.invalid", AccessTree = "{}",
            CompanyBranchID = new ShiftEntitySelectDTO { Value = "1", Text = "Synthetic Branch" }
        };
        var cut = context.Render<UserForm>(p => p.Add(x => x.Key, "42"));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=user-effective-permissions]")));

        // The effective-permissions view moved from the form toolbar into the Permissions section header.
        var permissions = cut.Find("[data-testid=user-section-permissions]");
        Assert.NotNull(permissions.QuerySelector("[data-testid=user-effective-permissions]"));
        Assert.Contains("View Effective Permissions", permissions.TextContent);
        Assert.Empty(cut.FindAll("header [data-testid=user-effective-permissions]"));

        cut.WaitForAssertion(() => Assert.Equal("synthetic-existing", cut.Find("[data-testid=user-section-security] input[type=text]").GetAttribute("value")));
        var security = cut.Find("[data-testid=user-section-security]");
        Assert.Contains("Authenticator App (TOTP)", security.TextContent);
        Assert.Contains("Enabled", security.TextContent);
    }
}
