using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Policy")]
public sealed class RecoveryContactPolicyTests
{
    [Fact]
    public void Lookup_initialization_does_not_attest_an_address_or_inherit_a_verification_flag()
    {
        var user = new User { ID = 1, Username = " Saved-User ", Email = " Saved@Example.Invalid ", EmailVerified = true };
        var state = new UserSecurityState { UserID = 1 };

        RecoveryContact.InitializeLookup(user, state);

        Assert.Equal("SAVED-USER", state.UsernameLookupKey);
        Assert.Equal("SAVED@EXAMPLE.INVALID", state.EmailLookupKey);
        Assert.False(RecoveryContact.IsEligible(user, state));
        RecoveryContact.PreserveVerifiedLegacyEmail(user, state);
        Assert.True(RecoveryContact.IsEligible(user, state));
        Assert.Equal(RecoveryEmailProvenance.VerifiedLegacyMigration, state.RecoveryEmailProvenance);
    }

    [Theory]
    [InlineData("email")]
    [InlineData("username")]
    [InlineData("revision")]
    [InlineData("subject")]
    public void Out_of_boundary_changes_cannot_reuse_contact_authority(string field)
    {
        var user = new User { ID = 1, Username = "saved-user", Email = "saved@example.invalid" };
        var state = new UserSecurityState { UserID = 1 };
        RecoveryContact.InitializeLookup(user, state);
        RecoveryContact.RecordOwnership(user, state, RecoveryEmailProvenance.TrustedAdminAssignment);
        switch (field)
        {
            case "email": user.Email = "another@example.invalid"; break;
            case "username": user.Username = "another-user"; break;
            case "revision": state.ContactRevision++; break;
            case "subject": state.UserID++; break;
        }

        Assert.False(RecoveryContact.IsEligible(user, state));
        if (field is "email" or "username" or "subject")
            Assert.ThrowsAny<Exception>(() => RecoveryContact.InitializeLookup(user, state));
    }

    [Fact]
    public void Email_change_invalidates_old_authority_and_returning_to_the_old_address_does_not_restore_its_revision()
    {
        var user = new User { ID = 1, Username = "saved-user", Email = "first@example.invalid", EmailVerified = true, PhoneVerified = true, IsActive = true };
        var state = new UserSecurityState { UserID = 1 };
        RecoveryContact.InitializeLookup(user, state);
        RecoveryContact.RecordOwnership(user, state, RecoveryEmailProvenance.TrustedAdminAssignment);
        var oldLink = new AuthenticationOperation { ID = Guid.NewGuid(), OutstandingLinkSlot = "1:verify", Destination = user.Email, HandleDigest = [1], State = AuthenticationOperationState.AwaitingExplicitSubmit };
        var audits = new List<AuthenticationAuditEvent>();
        var unit = new IdentitySecurityTransaction(user, state, new(), null, _ => { }, audits.Add, links: [oldLink]);

        Assert.True(RecoveryContact.ApplyAuthorizedEmailChange(unit, 1, 1, "second@example.invalid", RecoveryEmailProvenance.TrustedAdminAssignment, DateTimeOffset.UtcNow));
        Assert.False(user.EmailVerified);
        Assert.True(user.PhoneVerified);
        Assert.Equal(AuthenticationOperationState.Superseded, oldLink.State);
        Assert.Null(oldLink.OutstandingLinkSlot);
        Assert.Empty(oldLink.HandleDigest);
        Assert.True(RecoveryContact.IsEligible(user, state));
        Assert.True(RecoveryContact.ApplyAuthorizedEmailChange(unit, 2, 2, "first@example.invalid", RecoveryEmailProvenance.AuthenticatedContactChange, DateTimeOffset.UtcNow));
        Assert.Equal(3, state.SecurityVersion);
        Assert.Equal(3, state.ContactRevision);
        Assert.Equal(3, state.RecoveryEmailRevision);
        Assert.Equal(2, audits.Count);
        Assert.Throws<IdentitySecurityConflictException>(() => RecoveryContact.ApplyAuthorizedEmailChange(unit, 1, 1, "third@example.invalid", RecoveryEmailProvenance.TrustedAdminAssignment, DateTimeOffset.UtcNow));
        Assert.Equal("first@example.invalid", user.Email);
    }

    [Fact]
    public void Equivalent_email_spelling_does_not_change_or_reverify_the_saved_contact()
    {
        var user = new User { ID = 1, Username = "saved-user", Email = "first@example.invalid", EmailVerified = true, IsActive = true };
        var state = new UserSecurityState { UserID = 1 };
        RecoveryContact.InitializeLookup(user, state);
        var unit = new IdentitySecurityTransaction(user, state, new(), null, _ => { }, _ => { });

        Assert.False(RecoveryContact.ApplyAuthorizedEmailChange(unit, 1, 1, " FIRST@EXAMPLE.INVALID ", RecoveryEmailProvenance.TrustedAdminAssignment, DateTimeOffset.UtcNow));
        Assert.Equal("first@example.invalid", user.Email);
        Assert.True(user.EmailVerified);
        Assert.Equal(1, state.SecurityVersion);
        Assert.Equal(RecoveryEmailProvenance.Unknown, state.RecoveryEmailProvenance);
    }
}
