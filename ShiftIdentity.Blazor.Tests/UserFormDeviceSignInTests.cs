using Bunit;
using ShiftSoftware.ShiftBlazor.Components;
using ShiftSoftware.ShiftBlazor.Enums;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.User;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// An account that allows device sign-in never owes a password change at sign-in. On the production User form under
/// the identity authority, allowing device sign-in unticks and disables "Ask the user to change this password at next
/// sign-in", whichever of the two the administrator touches first, and turning it off enables the choice again without
/// ticking it. A save never posts both. Every expectation that follows a change waits for the render that carries it.
/// </summary>
[Trait("Category", "Ui"), Collection("User form")]
public sealed class UserFormDeviceSignInTests
{
    private const string PasswordInput = "[data-testid=user-password] input";
    private const string PasswordChoice = "[data-testid=user-password-require-change] input[type=checkbox]";
    private const string DeviceChoice = "[data-testid=user-allow-device-sign-in] input[type=checkbox], input[type=checkbox][data-testid=user-allow-device-sign-in]";
    private const string DeviceNote = "[data-testid=user-password-require-change-device-note]";
    private const string NewPassword = "Synthetic password 7";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Allowing_device_sign_in_unticks_and_disables_the_password_change_in_either_order(bool deviceFirst)
    {
        await using var context = UserFormHarness.Create(out var transport, staged: true);
        var cut = context.Render<UserForm>();
        var form = NewUser(cut);
        if (deviceFirst)
        {
            cut.Find(DeviceChoice).Change(true);
            cut.WaitForAssertion(() => Assert.True(form.Instance.Value.AllowDeviceSignIn));
            cut.Find(PasswordInput).Change(NewPassword);
        }
        else
        {
            cut.Find(PasswordInput).Change(NewPassword);
            // The new-account default is ticked until device sign-in is allowed.
            cut.WaitForAssertion(() => AssertChoice(cut, ticked: true, disabled: false));
            cut.Find(DeviceChoice).Change(true);
        }
        cut.WaitForAssertion(() => AssertChoice(cut, ticked: false, disabled: true));
        Assert.Equal("Not asked for while device sign-in is allowed.", cut.Find(DeviceNote).TextContent.Trim());

        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Single(transport.Posts));
        AssertPosted(transport.Posts.Single().Body, allowDeviceSignIn: true, requireChange: false);
    }

    [Fact]
    public async Task Turning_device_sign_in_off_enables_the_choice_and_leaves_it_unticked()
    {
        await using var context = UserFormHarness.Create(out var transport, staged: true);
        var cut = context.Render<UserForm>();
        NewUser(cut);
        cut.Find(PasswordInput).Change(NewPassword);
        cut.WaitForAssertion(() => AssertChoice(cut, ticked: true, disabled: false));
        cut.Find(DeviceChoice).Change(true);
        cut.WaitForAssertion(() => AssertChoice(cut, ticked: false, disabled: true));

        // The form never brings back the earlier value.
        cut.Find(DeviceChoice).Change(false);
        cut.WaitForAssertion(() => AssertChoice(cut, ticked: false, disabled: false));
        Assert.Empty(cut.FindAll(DeviceNote));

        // An administrator who wants the change ticks it explicitly, and the save carries it.
        cut.Find(PasswordChoice).Change(true);
        cut.WaitForAssertion(() => AssertChoice(cut, ticked: true, disabled: false));
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Single(transport.Posts));
        AssertPosted(transport.Posts.Single().Body, allowDeviceSignIn: false, requireChange: true);
    }

    [Fact]
    public async Task An_account_that_allows_device_sign_in_opens_with_the_choice_unticked_and_never_posts_both()
    {
        await using var context = UserFormHarness.Create(out var transport, staged: true);
        // The server never sends the choice back, so the loaded account carries the DTO's default: ticked.
        transport.Existing = new UserDTO
        {
            ID = "42", Username = "synthetic-screen", FullName = "Synthetic Screen", IsActive = true, AllowDeviceSignIn = true,
            AccessTree = "{}", CompanyBranchID = new ShiftEntitySelectDTO { Value = "1", Text = "Synthetic Branch" }
        };
        Assert.True(transport.Existing.RequireChangeAtNextLogin);
        var cut = context.Render<UserForm>(p => p.Add(x => x.Key, "42"));
        var form = cut.FindComponent<ShiftEntityForm<UserDTO>>();
        cut.WaitForAssertion(() => Assert.Equal("synthetic-screen", form.Instance.Value.Username));
        cut.WaitForState(() => form.Instance.TaskInProgress == FormTasks.None);
        await cut.InvokeAsync(form.Instance.EditItem);
        cut.WaitForAssertion(() => Assert.Equal(FormModes.Edit, form.Instance.Mode));

        // Setting a new password for a screen account.
        cut.Find(PasswordInput).Change(NewPassword);
        cut.WaitForAssertion(() => AssertChoice(cut, ticked: false, disabled: true));
        Assert.Single(cut.FindAll(DeviceNote));
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Single(transport.Posts));
        var (path, body) = transport.Posts.Single();
        Assert.Equal("/IdentityUser/42", path);
        AssertPosted(body, allowDeviceSignIn: true, requireChange: false);
    }

    // A new user with the validator's other required fields. The branch picker is a remote autocomplete, so it is set on the bound model.
    private static IRenderedComponent<ShiftEntityForm<UserDTO>> NewUser(IRenderedComponent<UserForm> cut)
    {
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(PasswordInput)));
        var form = cut.FindComponent<ShiftEntityForm<UserDTO>>();
        form.Instance.Value.Username = "synthetic-screen"; form.Instance.Value.FullName = "Synthetic Screen";
        form.Instance.Value.CompanyBranchID = new ShiftEntitySelectDTO { Value = "1", Text = "Synthetic Branch" };
        return form;
    }

    private static void AssertChoice(IRenderedComponent<UserForm> cut, bool ticked, bool disabled)
    {
        var choice = cut.Find(PasswordChoice);
        Assert.Equal(ticked, choice.HasAttribute("checked"));
        Assert.Equal(disabled, choice.HasAttribute("disabled"));
    }

    private static void AssertPosted(string body, bool allowDeviceSignIn, bool requireChange)
    {
        Assert.Contains($"\"allowDeviceSignIn\":{(allowDeviceSignIn ? "true" : "false")}", body);
        Assert.Contains($"\"requireChangeAtNextLogin\":{(requireChange ? "true" : "false")}", body);
        Assert.Contains($"\"password\":\"{NewPassword}\"", body);
    }
}
