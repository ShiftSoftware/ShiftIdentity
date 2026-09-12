using Bunit;
using ShiftSoftware.ShiftBlazor.Components;
using ShiftSoftware.ShiftBlazor.Enums;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.User;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// The production User form's two choices: each is offered only while it applies (a password being set, a new or
/// changed address), checked by default, and posted with the legacy UserDTO exactly as chosen through the real
/// ShiftEntityForm save path against a scripted HTTP transport.
/// </summary>
[Trait("Category", "Ui")]
public sealed class UserFormCheckboxTests
{
    private const string PasswordInput = "[data-testid=user-password] input";
    private const string EmailInput = "[data-testid=user-email] input";
    private const string PasswordChoice = "[data-testid=user-password-require-change] input[type=checkbox]";
    private const string EmailChoice = "[data-testid=user-email-send-verification] input[type=checkbox]";

    [Fact]
    public async Task A_new_user_offers_each_choice_only_while_its_field_has_a_value()
    {
        await using var context = UserFormHarness.Create(out var transport);
        var cut = context.Render<UserForm>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(PasswordInput)));
        Assert.Empty(cut.FindAll(PasswordChoice));
        Assert.Empty(cut.FindAll(EmailChoice));

        cut.Find(PasswordInput).Change("Synthetic password 7");
        Assert.True(cut.Find(PasswordChoice).HasAttribute("checked"));
        Assert.Contains("Ask the user to change this password at next sign-in", cut.Markup);
        Assert.Empty(cut.FindAll(EmailChoice));

        cut.Find(EmailInput).Change("new@example.invalid");
        Assert.True(cut.Find(EmailChoice).HasAttribute("checked"));
        Assert.Contains("Send a verification link to this address", cut.Markup);

        cut.Find(PasswordInput).Change("");
        Assert.Empty(cut.FindAll(PasswordChoice));
        cut.Find(EmailInput).Change("");
        Assert.Empty(cut.FindAll(EmailChoice));
        Assert.Empty(transport.Posts);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Save_posts_the_form_choices_with_the_legacy_user(bool requireChange, bool sendVerification)
    {
        await using var context = UserFormHarness.Create(out var transport);
        var cut = context.Render<UserForm>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(PasswordInput)));
        var form = cut.FindComponent<ShiftEntityForm<UserDTO>>();
        // The validator's other required fields; the branch picker is a remote autocomplete, so it is set on the bound model.
        form.Instance.Value.Username = "synthetic-form"; form.Instance.Value.FullName = "Synthetic User";
        form.Instance.Value.CompanyBranchID = new ShiftEntitySelectDTO { Value = "1", Text = "Synthetic Branch" };
        cut.Find(PasswordInput).Change("Synthetic password 7");
        cut.Find(EmailInput).Change("new@example.invalid");
        if (!requireChange) cut.Find(PasswordChoice).Change(false);
        if (!sendVerification) cut.Find(EmailChoice).Change(false);
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Single(transport.Posts));
        var post = transport.Posts.Single();
        Assert.Equal("/IdentityUser", post.Path);
        Assert.Contains($"\"requireChangeAtNextLogin\":{(requireChange ? "true" : "false")}", post.Body);
        Assert.Contains($"\"sendVerification\":{(sendVerification ? "true" : "false")}", post.Body);
        Assert.Contains("\"password\":\"Synthetic password 7\"", post.Body);
        Assert.Contains("\"email\":\"new@example.invalid\"", post.Body);
    }

    [Fact]
    public async Task An_existing_user_offers_no_choice_until_a_password_is_entered_or_the_address_changes()
    {
        await using var context = UserFormHarness.Create(out var transport);
        transport.Existing = new UserDTO
        {
            ID = "42", Username = "synthetic-existing", FullName = "Synthetic Existing", IsActive = true,
            Email = "existing@example.invalid", AccessTree = "{}",
            CompanyBranchID = new ShiftEntitySelectDTO { Value = "1", Text = "Synthetic Branch" }
        };
        var cut = context.Render<UserForm>(p => p.Add(x => x.Key, "42"));
        cut.WaitForAssertion(() => Assert.Equal("existing@example.invalid", cut.Find(EmailInput).GetAttribute("value")));
        // View mode offers nothing, so opening a user never suggests a send or a forced change.
        Assert.Empty(cut.FindAll(PasswordChoice));
        Assert.Empty(cut.FindAll(EmailChoice));

        var form = cut.FindComponent<ShiftEntityForm<UserDTO>>();
        cut.WaitForState(() => form.Instance.TaskInProgress == FormTasks.None);
        await cut.InvokeAsync(form.Instance.EditItem);
        cut.WaitForAssertion(() => Assert.Equal(FormModes.Edit, form.Instance.Mode));
        // Edit mode with the saved address offers nothing either: changing the name or permissions sends no email.
        Assert.Empty(cut.FindAll(PasswordChoice));
        Assert.Empty(cut.FindAll(EmailChoice));

        cut.Find(EmailInput).Change("changed@example.invalid");
        Assert.True(cut.Find(EmailChoice).HasAttribute("checked"));
        // Back to the saved address, in another case: the hook compares case-insensitively, so nothing would be sent.
        cut.Find(EmailInput).Change("Existing@Example.invalid");
        Assert.Empty(cut.FindAll(EmailChoice));

        cut.Find(PasswordInput).Change("Synthetic password 7");
        Assert.True(cut.Find(PasswordChoice).HasAttribute("checked"));
        Assert.Empty(transport.Posts);
    }
}
