using System.Net;
using System.Text.Json;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Mappers;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Policy")]
public sealed class ProfileAuthorityTests
{
    [Theory]
    [InlineData("username", "other-user", "Username")]
    [InlineData("username", null, "Username")]
    [InlineData("email", "other@example.invalid", "ContactReauthenticationRequired")]
    [InlineData("email", null, "ContactReauthenticationRequired")]
    [InlineData("email", "person+other@example.invalid", "ContactReauthenticationRequired")]
    [InlineData("email", "per.son@example.invalid", "ContactReauthenticationRequired")]
    [InlineData("phone", "+12025550199", "ContactReauthenticationRequired")]
    [InlineData("phone", null, "ContactReauthenticationRequired")]
    [InlineData("phone", "invalid phone", "ContactReauthenticationRequired")]
    public void Sensitive_change_refuses_before_any_ordinary_or_security_write(string field, string? value, string error)
    {
        var user = SavedUser();
        var dto = OrdinaryEdit(user);
        switch (field)
        {
            case "username": dto.Username = value!; break;
            case "email": dto.Email = value; break;
            case "phone": dto.Phone = value; break;
        }

        var exception = Assert.Throws<ShiftEntityException>(() => dto.ApplyProfileEdits(user));

        Assert.Equal((int)HttpStatusCode.BadRequest, exception.HttpStatusCode);
        Assert.Equal(error, exception.Message.For);
        Assert.Equal("Saved name", user.FullName);
        Assert.Equal(new DateTime(1990, 1, 1), user.BirthDate);
        Assert.Equal("[]", user.Signature);
        AssertSavedSecurity(user);
        var envelope = JsonSerializer.Serialize(new ShiftEntityResponse<UserDataDTO> { Message = exception.Message });
        Assert.Contains(error, envelope);
    }

    [Fact]
    public void Unchanged_legacy_fields_allow_only_ordinary_profile_edits()
    {
        var user = SavedUser();
        var dto = OrdinaryEdit(user);
        dto.Signature = [new ShiftFileDTO { Name = "signature.png", ContainerName = "signatures", Blob = "synthetic-signature", ContentType = "image/png" }];

        dto.ApplyProfileEdits(user);

        Assert.Equal("Updated name", user.FullName);
        Assert.Equal(new DateTime(1992, 2, 2), user.BirthDate);
        Assert.Equal("synthetic-signature", Assert.Single(user.Signature.ToShiftFiles()!).Blob);
        AssertSavedSecurity(user);
    }

    [Fact]
    public void A_profile_payload_cannot_verify_unverified_contacts()
    {
        var user = SavedUser();
        user.EmailVerified = false;
        user.PhoneVerified = false;
        var dto = OrdinaryEdit(user);
        dto.EmailVerified = true;
        dto.PhoneVerified = true;

        dto.ApplyProfileEdits(user);

        Assert.False(user.EmailVerified);
        Assert.False(user.PhoneVerified);
        Assert.Equal("existing-grant", user.VerificationSASToken);
        Assert.Equal("Updated name", user.FullName);
    }

    [Fact]
    public void Equivalent_contact_representations_are_accepted_without_rewriting_saved_values()
    {
        var user = SavedUser();
        var dto = OrdinaryEdit(user);
        dto.Username = " SYNTHETIC-USER ";
        dto.Email = " PERSON@EXAMPLE.INVALID ";
        dto.Phone = "+1 (202) 555-0123";

        dto.ApplyProfileEdits(user);

        Assert.Equal("Updated name", user.FullName);
        AssertSavedSecurity(user);
    }

    [Fact]
    public void Unchanged_historical_phone_does_not_become_a_new_contact_assignment()
    {
        var user = SavedUser();
        user.Phone = "historical phone";
        var dto = OrdinaryEdit(user);

        dto.ApplyProfileEdits(user);

        Assert.Equal("historical phone", user.Phone);
        Assert.True(user.PhoneVerified);
        Assert.Equal("Updated name", user.FullName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Absent_contacts_remain_absent(string? supplied)
    {
        var user = SavedUser();
        user.Email = null;
        user.Phone = null;
        var dto = OrdinaryEdit(user);
        dto.Email = supplied;
        dto.Phone = supplied;

        dto.ApplyProfileEdits(user);

        Assert.Null(user.Email);
        Assert.Null(user.Phone);
        Assert.Equal("Updated name", user.FullName);
    }

    [Fact]
    public void Adding_a_contact_to_an_account_without_one_requires_the_separate_flow()
    {
        var user = SavedUser();
        user.Email = null;
        var dto = OrdinaryEdit(user);
        dto.Email = "person@example.invalid";

        var exception = Assert.Throws<ShiftEntityException>(() => dto.ApplyProfileEdits(user));

        Assert.Equal("ContactReauthenticationRequired", exception.Message.For);
        Assert.Null(user.Email);
        Assert.Equal("Saved name", user.FullName);
    }

    private static User SavedUser() => new()
    {
        ID = 71, Username = "synthetic-user", Email = "person@example.invalid", Phone = "+12025550123",
        FullName = "Saved name", BirthDate = new DateTime(1990, 1, 1), Signature = "[]",
        EmailVerified = true, PhoneVerified = true, IsActive = true,
        PasswordHash = [1, 2, 3], Salt = [4, 5, 6], VerificationSASToken = "existing-grant", RequireChangePassword = true
    };

    private static UserDataDTO OrdinaryEdit(User user) => new()
    {
        ID = "999", Username = user.Username, Email = user.Email, Phone = user.Phone,
        FullName = "Updated name", BirthDate = new DateTime(1992, 2, 2), Signature = [],
        EmailVerified = false, PhoneVerified = false, IsDeleted = true
    };

    private static void AssertSavedSecurity(User user)
    {
        Assert.Equal(71, user.ID);
        Assert.Equal("synthetic-user", user.Username);
        Assert.Equal("person@example.invalid", user.Email);
        Assert.Equal("+12025550123", user.Phone);
        Assert.True(user.EmailVerified);
        Assert.True(user.PhoneVerified);
        Assert.False(user.IsDeleted);
        Assert.True(user.IsActive);
        Assert.True(user.RequireChangePassword);
        Assert.Equal(new byte[] { 1, 2, 3 }, user.PasswordHash);
        Assert.Equal(new byte[] { 4, 5, 6 }, user.Salt);
        Assert.Equal("existing-grant", user.VerificationSASToken);
    }
}
