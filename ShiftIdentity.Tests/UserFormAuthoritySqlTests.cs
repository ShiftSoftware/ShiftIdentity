using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.ValidatorsAndFormatters;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// The legacy administrator writers on the staged v2 boundary: the production api/IdentityUser save and delete,
/// AssignRandomPasswords, VerifyPhones and import, all through the real dashboard registration with the staged
/// authority registered on the same scoped context. Every security-relevant change is admitted in the save
/// transaction, increments the target's SecurityVersion once, ends its v2 sessions and is audited with the operator.
/// </summary>
[Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class UserFormAuthoritySqlTests(SqlIdentityFixture fixture) : IClassFixture<SqlIdentityFixture>, IAsyncLifetime
{
    private const string AdminName = "synthetic-authority-admin";
    // Users read/write/delete for the routes, plus wildcard data-level access on every dimension the User row carries.
    private const string AdminTree = "{\"ShiftIdentityActions\":{\"Users\":[\"r\",\"w\",\"d\"],\"AccessTrees\":[\"r\"],\"DataLevelAccess\":{\"Countries\":[\"r\",\"w\",\"d\"],\"Regions\":[\"r\",\"w\",\"d\"],\"Companies\":[\"r\",\"w\",\"d\"],\"Branches\":[\"r\",\"w\",\"d\"]}}}";
    private const string ReadOnlyTree = "{\"ShiftIdentityActions\":{\"Users\":[\"r\"]}}";
    private const string Password = "Form chosen phrase 61!";
    private const string OtherPassword = "Another chosen phrase 62!";
    // Unique valid mobile numbers per test: the class shares one database, and phones are unique across users.
    private static string Phone => "+96477" + Random.Shared.Next(10000000, 99999999).ToString(System.Globalization.CultureInfo.InvariantCulture);
    private string branchID = "";
    private long adminID;
    private ControlledClock clock = null!;

    public async ValueTask InitializeAsync()
    {
        clock = new(DateTimeOffset.UtcNow); fixture.Clock = clock;
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var template = await db.Users.AsNoTracking().SingleAsync(x => x.ID == fixture.UserID);
        branchID = template.CompanyBranchID!.Value.ToString();
        adminID = await db.Users.IgnoreQueryFilters().Where(x => x.Username == AdminName).Select(x => x.ID).SingleOrDefaultAsync();
        if (adminID == 0) adminID = await fixture.CreateSyntheticUserAsync(AdminName, AdminTree);
        await db.Users.IgnoreQueryFilters().Where(x => x.ID == adminID).ExecuteUpdateAsync(x => x
            .SetProperty(u => u.IsActive, true).SetProperty(u => u.IsDeleted, false).SetProperty(u => u.AccessTree, AdminTree));
        await db.Set<UserSecurityState>().Where(x => x.UserID == adminID).ExecuteUpdateAsync(x => x
            .SetProperty(s => s.SecurityVersion, 1).SetProperty(s => s.FailedProofs, 0).SetProperty(s => s.FailureWindowStart, (DateTimeOffset?)null));
    }

    public ValueTask DisposeAsync() { fixture.Clock = TimeProvider.System; return ValueTask.CompletedTask; }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Create_stores_the_versioned_credential_records_security_state_and_delivers_through_the_staged_path(bool send, bool requireChange)
    {
        using var host = await HostAsync();
        var dto = NewUser(email: $"created-{Guid.NewGuid():N}@example.invalid", requireChange: requireChange, sendVerification: send);
        using var response = await host.Client.PostAsJsonAsync("/api/IdentityUser", dto);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var envelope = (await response.Content.ReadFromJsonAsync<ShiftEntityResponse<UserDTO>>())!;
        var id = long.Parse(envelope.Entity!.ID!);
        Assert.Equal(send ? "Requested" : null, envelope.Additional?["EmailVerification"]?.ToString());
        var (user, state) = await StateAsync(id);
        Assert.True(HashService.VerifyVersionedPassword(Password, user.Salt, user.PasswordHash));
        Assert.False(HashService.VerifyPassword(Password, user.Salt, user.PasswordHash));
        Assert.Equal(requireChange, user.RequireChangePassword);
        Assert.False(user.EmailVerified); Assert.Null(user.VerificationSASToken);
        Assert.Equal(1, state.SecurityVersion); Assert.Equal(1, state.ContactRevision);
        Assert.Equal(dto.Username.ToUpperInvariant(), state.UsernameLookupKey); Assert.Equal(dto.Email!.ToUpperInvariant(), state.EmailLookupKey);
        Assert.Equal(RecoveryEmailProvenance.TrustedAdminAssignment, state.RecoveryEmailProvenance);
        Assert.True(RecoveryContact.IsEligible(user, state));
        var audit = Assert.Single(await AuditsAsync(id, "EmailVerificationRequested", "SecurityDeliveryAccepted", "SecurityDeliveryUnconfirmed"));
        Assert.Equal(("AccountCreated", adminID, 1L), audit);
        // The legacy SAS sender is not used; the staged sink receives the link only when requested.
        Assert.Empty(host.Verifications.Sent);
        if (send)
        {
            var message = Assert.Single(Inbox.Messages);
            Assert.Equal(dto.Email, message.Destination); Assert.Equal("Verify your email", message.Subject);
        }
        else Assert.Empty(Inbox.Messages);
        var login = await LoginAsync(host, dto.Username, Password);
        if (requireChange) Assert.Equal(AuthenticationStep.PasswordChange, Assert.IsType<ChallengeRequired>(login).Challenge.Step);
        else Assert.IsType<SessionIssued>(login);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Update_password_increments_the_version_ends_sessions_and_forces_change_only_when_requested(bool requireChange)
    {
        using var host = await HostAsync();
        var id = await CreateAsync(host, NewUser(requireChange: false, sendVerification: false));
        var session = Assert.IsType<SessionIssued>(await LoginAsync(host, (await GetAsync(host, id)).Username, Password));
        var dto = await GetAsync(host, id);
        dto.Password = OtherPassword; dto.RequireChangeAtNextLogin = requireChange;
        await PutAsync(host, dto);
        var (user, state) = await StateAsync(id);
        Assert.Equal(2, state.SecurityVersion);
        Assert.True(HashService.VerifyVersionedPassword(OtherPassword, user.Salt, user.PasswordHash));
        Assert.Equal(requireChange, user.RequireChangePassword);
        Assert.Equal((requireChange ? "AdminPasswordSetRequiringChange" : "AdminPasswordSet", adminID, 2L), Assert.Single(await AuditsAsync(id, "AccountCreated")));
        Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await RefreshAsync(host, session.Session.RefreshToken)).Code);
        Assert.Equal(AuthenticationFailure.InvalidProof, Assert.IsType<AuthenticationRefused>(await LoginAsync(host, dto.Username, Password)).Code);
        var login = await LoginAsync(host, dto.Username, OtherPassword);
        if (requireChange) Assert.Equal(AuthenticationStep.PasswordChange, Assert.IsType<ChallengeRequired>(login).Challenge.Step);
        else Assert.IsType<SessionIssued>(login);
        Assert.Empty(host.Verifications.Sent); Assert.Empty(Inbox.Messages);
    }

    [Fact]
    public async Task Update_username_renames_the_login_and_lookup_and_a_duplicate_saves_nothing()
    {
        using var host = await HostAsync();
        var id = await CreateAsync(host, NewUser(requireChange: false, sendVerification: false));
        var otherID = await CreateAsync(host, NewUser(requireChange: false, sendVerification: false));
        var otherName = (await GetAsync(host, otherID)).Username;
        var session = Assert.IsType<SessionIssued>(await LoginAsync(host, (await GetAsync(host, id)).Username, Password));
        var dto = await GetAsync(host, id);
        var previous = dto.Username;
        var renamed = "Renamed-" + Guid.NewGuid().ToString("N")[..8];
        dto.Username = "  " + renamed + "  ";
        await PutAsync(host, dto);
        var (user, state) = await StateAsync(id);
        Assert.Equal(renamed, user.Username); Assert.Equal(renamed.ToUpperInvariant(), state.UsernameLookupKey);
        Assert.True(RecoveryContact.LookupMatches(user, state));
        Assert.Equal(2, state.SecurityVersion);
        Assert.Equal(("AdminUsernameChanged", adminID, 2L), Assert.Single(await AuditsAsync(id, "AccountCreated")));
        Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await RefreshAsync(host, session.Session.RefreshToken)).Code);
        Assert.Equal(AuthenticationFailure.InvalidProof, Assert.IsType<AuthenticationRefused>(await LoginAsync(host, previous, Password)).Code);
        Assert.IsType<SessionIssued>(await LoginAsync(host, renamed, Password));

        // A duplicate (case-insensitive) is refused with a field message, and the ordinary edit in the same save is not kept.
        var duplicate = await GetAsync(host, id);
        duplicate.Username = otherName.ToUpperInvariant(); duplicate.FullName = "Synthetic Form User (renamed)";
        using var refused = await host.Client.PutAsJsonAsync($"/api/IdentityUser/{id}", duplicate);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var message = (await refused.Content.ReadFromJsonAsync<ShiftEntityResponse<UserDTO>>())!.Message!;
        Assert.Equal("Duplicate", message.Title); Assert.Equal(nameof(UserDTO.Username), message.For);
        var (after, afterState) = await StateAsync(id);
        Assert.Equal(renamed, after.Username); Assert.Equal("Synthetic Form User", after.FullName); Assert.Equal(2, afterState.SecurityVersion);
    }

    [Theory]
    [InlineData("changed", true)]
    [InlineData("changed", false)]
    [InlineData("failing", true)]
    [InlineData("unchanged", true)]
    [InlineData("removed", true)]
    public async Task Update_email_clears_verification_records_admin_authority_and_delivers_only_when_requested(string change, bool send)
    {
        using var host = await HostAsync();
        var saved = $"saved-{Guid.NewGuid():N}@example.invalid";
        var id = await CreateAsync(host, NewUser(email: saved, requireChange: false, sendVerification: false));
        await using (var setup = fixture.CreateContext())
            await setup.Users.Where(x => x.ID == id).ExecuteUpdateAsync(x => x.SetProperty(u => u.EmailVerified, true));
        var session = Assert.IsType<SessionIssued>(await LoginAsync(host, (await GetAsync(host, id)).Username, Password));
        Inbox.FailDeliveries = change == "failing";
        var dto = await GetAsync(host, id);
        var requested = change switch
        {
            "changed" or "failing" => $"changed-{Guid.NewGuid():N}@example.invalid",
            "removed" => null,
            _ => saved.ToUpperInvariant()
        };
        dto.Email = requested; dto.SendVerification = send;
        using var response = await host.Client.PutAsJsonAsync($"/api/IdentityUser/{id}", dto);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var envelope = (await response.Content.ReadFromJsonAsync<ShiftEntityResponse<UserDTO>>())!;
        var (user, state) = await StateAsync(id);
        Assert.Empty(host.Verifications.Sent);
        switch (change)
        {
            case "unchanged":
                Assert.Equal(saved, user.Email); Assert.True(user.EmailVerified);
                Assert.Equal(1, state.SecurityVersion); Assert.Equal(1, state.ContactRevision);
                Assert.Empty(await AuditsAsync(id, "AccountCreated")); Assert.Empty(Inbox.Messages);
                Assert.Null(envelope.Additional);
                Assert.IsType<SessionIssued>(await RefreshAsync(host, session.Session.RefreshToken));
                return;
            case "removed":
                Assert.Null(user.Email); Assert.False(user.EmailVerified); Assert.Null(state.EmailLookupKey);
                Assert.Equal(RecoveryEmailProvenance.Unknown, state.RecoveryEmailProvenance); Assert.Null(state.RecoveryEmail);
                Assert.Empty(Inbox.Messages); Assert.Null(envelope.Additional);
                break;
            default:
                Assert.Equal(requested, user.Email); Assert.False(user.EmailVerified);
                Assert.Equal(requested!.ToUpperInvariant(), state.EmailLookupKey);
                Assert.Equal(RecoveryEmailProvenance.TrustedAdminAssignment, state.RecoveryEmailProvenance);
                Assert.True(RecoveryContact.IsEligible(user, state));
                if (!send) { Assert.Empty(Inbox.Messages); Assert.Null(envelope.Additional); }
                else if (change == "failing")
                {
                    Assert.Equal("Unconfirmed", envelope.Additional!["EmailVerification"].ToString()); Assert.Empty(Inbox.Messages);
                }
                else
                {
                    Assert.Equal("Requested", envelope.Additional!["EmailVerification"].ToString());
                    Assert.Equal(requested, Assert.Single(Inbox.Messages).Destination);
                }
                break;
        }
        Assert.Equal(2, state.SecurityVersion); Assert.Equal(2, state.ContactRevision);
        Assert.Equal(("AdminEmailChanged", adminID, 2L), Assert.Single(await AuditsAsync(id, "AccountCreated", "EmailContactChanged", "EmailVerificationRequested", "SecurityDeliveryAccepted", "SecurityDeliveryUnconfirmed")));
        Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await RefreshAsync(host, session.Session.RefreshToken)).Code);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("unchanged")]
    public async Task Update_phone_increments_the_version_and_clears_its_verification_only_when_it_changes(string change)
    {
        using var host = await HostAsync();
        var phone = Phone; var otherPhone = Phone;
        var id = await CreateAsync(host, NewUser(requireChange: false, sendVerification: false, phone: phone));
        await using (var setup = fixture.CreateContext())
            await setup.Users.Where(x => x.ID == id).ExecuteUpdateAsync(x => x.SetProperty(u => u.PhoneVerified, true));
        var session = Assert.IsType<SessionIssued>(await LoginAsync(host, (await GetAsync(host, id)).Username, Password));
        var dto = await GetAsync(host, id);
        dto.Phone = change == "changed" ? otherPhone : dto.Phone;
        await PutAsync(host, dto);
        var (user, state) = await StateAsync(id);
        if (change == "changed")
        {
            Assert.Equal(PhoneNumber.GetFormattedPhone(otherPhone), user.Phone); Assert.False(user.PhoneVerified);
            Assert.Equal(2, state.SecurityVersion); Assert.Equal(1, state.ContactRevision);
            Assert.Equal(("AdminPhoneChanged", adminID, 2L), Assert.Single(await AuditsAsync(id, "AccountCreated")));
            Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await RefreshAsync(host, session.Session.RefreshToken)).Code);
        }
        else
        {
            Assert.Equal(PhoneNumber.GetFormattedPhone(phone), user.Phone); Assert.True(user.PhoneVerified);
            Assert.Equal(1, state.SecurityVersion); Assert.Empty(await AuditsAsync(id, "AccountCreated"));
            Assert.IsType<SessionIssued>(await RefreshAsync(host, session.Session.RefreshToken));
        }
    }

    [Fact]
    public async Task Update_active_status_increments_the_version_and_ends_sessions_in_both_directions()
    {
        using var host = await HostAsync();
        var id = await CreateAsync(host, NewUser(requireChange: false, sendVerification: false));
        var username = (await GetAsync(host, id)).Username;
        var session = Assert.IsType<SessionIssued>(await LoginAsync(host, username, Password));
        var dto = await GetAsync(host, id); dto.IsActive = false;
        await PutAsync(host, dto);
        var (_, off) = await StateAsync(id);
        Assert.Equal(2, off.SecurityVersion);
        Assert.Equal(AuthenticationFailure.AccountUnavailable, Assert.IsType<AuthenticationRefused>(await RefreshAsync(host, session.Session.RefreshToken)).Code);
        Assert.Equal(AuthenticationFailure.AccountUnavailable, Assert.IsType<AuthenticationRefused>(await LoginAsync(host, username, Password)).Code);
        // An inactive account still accepts a correction in the same save that reactivates it.
        dto = await GetAsync(host, id); dto.IsActive = true; dto.Password = OtherPassword; dto.RequireChangeAtNextLogin = false;
        await PutAsync(host, dto);
        var (user, on) = await StateAsync(id);
        Assert.True(user.IsActive); Assert.Equal(3, on.SecurityVersion);
        Assert.Equal(new[] { "AccountActivated", "AccountDeactivated", "AdminPasswordSet" }, (await AuditsAsync(id, "AccountCreated")).Select(x => x.Outcome).Order());
        Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await RefreshAsync(host, session.Session.RefreshToken)).Code);
        Assert.IsType<SessionIssued>(await LoginAsync(host, username, OtherPassword));
    }

    [Fact]
    public async Task Update_permissions_increments_the_version_when_stored_grants_or_assigned_trees_change()
    {
        using var host = await HostAsync();
        long treeID;
        await using (var setup = fixture.CreateContext())
        {
            var tree = new AccessTree { Name = "Synthetic assigned " + Guid.NewGuid().ToString("N")[..8], Tree = ReadOnlyTree };
            setup.AccessTrees.Add(tree); await setup.SaveChangesAsync(); treeID = tree.ID;
        }
        var id = await CreateAsync(host, NewUser(requireChange: false, sendVerification: false));
        var username = (await GetAsync(host, id)).Username;
        var session = Assert.IsType<SessionIssued>(await LoginAsync(host, username, Password));

        // Same permissions, other spelling: the ordinary edit saves without admission.
        var dto = await GetAsync(host, id); dto.FullName = "Synthetic Form User (edited)"; dto.AccessTree = "{ }";
        await PutAsync(host, dto);
        var (user, state) = await StateAsync(id);
        Assert.Equal("Synthetic Form User (edited)", user.FullName); Assert.Equal(1, state.SecurityVersion);
        Assert.Empty(await AuditsAsync(id, "AccountCreated"));
        Assert.IsType<SessionIssued>(await RefreshAsync(host, session.Session.RefreshToken));

        // A user-specific grant changes the stored permissions.
        dto = await GetAsync(host, id); dto.AccessTree = ReadOnlyTree;
        await PutAsync(host, dto);
        (user, state) = await StateAsync(id);
        Assert.Equal(2, state.SecurityVersion); Assert.Contains("Users", user.AccessTree);
        Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await RefreshAsync(host, session.Session.RefreshToken)).Code);

        // An assigned tree and its removal each increment the version, even if grants overlap.
        session = Assert.IsType<SessionIssued>(await LoginAsync(host, username, Password));
        dto = await GetAsync(host, id); dto.AccessTrees = [new ShiftEntitySelectDTO { Value = treeID.ToString() }];
        await PutAsync(host, dto);
        Assert.Equal(3, (await StateAsync(id)).State.SecurityVersion);
        Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await RefreshAsync(host, session.Session.RefreshToken)).Code);
        dto = await GetAsync(host, id);
        Assert.Equal(treeID.ToString(), Assert.Single(dto.AccessTrees).Value);
        dto.AccessTrees = [];
        await PutAsync(host, dto);
        (user, state) = await StateAsync(id);
        Assert.Equal(4, state.SecurityVersion);
        Assert.Equal(3, (await AuditsAsync(id, "AccountCreated")).Count(x => x.Outcome == "AdminPermissionsChanged"));
        await using var verify = fixture.CreateContext();
        Assert.Empty(await verify.UserAccessTrees.Where(x => x.UserID == id).ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{\"ShiftIdentityActions\":{\"Users\":[\"r\",\"r\"]}}")]
    [InlineData("{\"ShiftIdentityActions\":{\"Users\":[2,1,1],\"DataLevelAccess\":{\"Branches\":{\"42\":[1,1]}}}}")]
    public async Task Equivalent_stored_grants_keep_the_security_version_and_session(string? storedTree)
    {
        using var host = await HostAsync();
        var id = await CreateAsync(host, NewUser(requireChange: false, sendVerification: false));
        await using (var setup = fixture.CreateContext())
            await setup.Users.Where(x => x.ID == id).ExecuteUpdateAsync(x => x.SetProperty(u => u.AccessTree, storedTree));

        var dto = await GetAsync(host, id);
        var session = Assert.IsType<SessionIssued>(await LoginAsync(host, dto.Username, Password));
        dto.FullName = "Synthetic Form User (edited)";
        await PutAsync(host, dto);

        var (user, state) = await StateAsync(id);
        Assert.Equal("Synthetic Form User (edited)", user.FullName);
        Assert.Equal(1, state.SecurityVersion);
        Assert.Empty(await AuditsAsync(id, "AccountCreated"));
        Assert.IsType<SessionIssued>(await RefreshAsync(host, session.Session.RefreshToken));
    }

    [Fact]
    public async Task Ordinary_edits_are_saved_without_admission_or_a_version_change()
    {
        var points = new List<string>();
        using var host = await HostAsync(observe: points.Add);
        var id = await CreateAsync(host, NewUser(requireChange: false, sendVerification: false));
        var session = Assert.IsType<SessionIssued>(await LoginAsync(host, (await GetAsync(host, id)).Username, Password));
        points.Clear();
        var dto = await GetAsync(host, id);
        dto.FullName = "Synthetic Form User (edited)"; dto.BirthDate = new DateTime(1990, 1, 2);
        await PutAsync(host, dto);
        var (user, state) = await StateAsync(id);
        Assert.Equal("Synthetic Form User (edited)", user.FullName); Assert.Equal(new DateTime(1990, 1, 2), user.BirthDate);
        Assert.Equal(1, state.SecurityVersion);
        Assert.Empty(await AuditsAsync(id, "AccountCreated"));
        Assert.DoesNotContain("LegacyAdmission", points);
        Assert.IsType<SessionIssued>(await RefreshAsync(host, session.Session.RefreshToken));
    }

    [Fact]
    public async Task Delete_increments_the_version_audits_the_operator_and_refuses_the_account()
    {
        using var host = await HostAsync();
        var id = await CreateAsync(host, NewUser(requireChange: false, sendVerification: false));
        var username = (await GetAsync(host, id)).Username;
        var session = Assert.IsType<SessionIssued>(await LoginAsync(host, username, Password));
        using var response = await host.Client.DeleteAsync($"/api/IdentityUser/{id}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var (user, state) = await StateAsync(id);
        Assert.True(user.IsDeleted); Assert.Equal(2, state.SecurityVersion);
        Assert.Equal(("AccountDeleted", adminID, 2L), Assert.Single(await AuditsAsync(id, "AccountCreated")));
        Assert.Equal(AuthenticationFailure.AccountUnavailable, Assert.IsType<AuthenticationRefused>(await RefreshAsync(host, session.Session.RefreshToken)).Code);
        Assert.Equal(AuthenticationFailure.AccountUnavailable, Assert.IsType<AuthenticationRefused>(await LoginAsync(host, username, Password)).Code);
    }

    [Theory]
    [InlineData("self", HttpStatusCode.Forbidden)]
    [InlineData("permission", HttpStatusCode.Forbidden)]
    [InlineData("stale-proof", HttpStatusCode.Forbidden)]
    [InlineData("protected", HttpStatusCode.Forbidden)]
    public async Task Sensitive_changes_are_refused_for_an_ineligible_operator_or_target_and_save_nothing(string scenario, HttpStatusCode expected)
    {
        using var host = await HostAsync(legacyToken: scenario != "stale-proof");
        var id = scenario == "self" ? adminID : await CreateAsync(host, NewUser(requireChange: false, sendVerification: false));
        var before = await StateAsync(id);
        try
        {
            if (scenario == "permission")
                await using (var setup = fixture.CreateContext())
                    await setup.Users.Where(x => x.ID == adminID).ExecuteUpdateAsync(x => x.SetProperty(u => u.AccessTree, ReadOnlyTree));
            if (scenario == "protected")
                await using (var setup = fixture.CreateContext())
                    await setup.Users.Where(x => x.ID == id).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsProtected, true));
            if (scenario == "stale-proof") clock.Advance(TimeSpan.FromMinutes(6));
            var dto = await GetAsync(host, id);
            dto.Password = OtherPassword; dto.FullName = "Synthetic Form User (edited)";
            using var response = await host.Client.PutAsJsonAsync($"/api/IdentityUser/{id}", dto);
            Assert.Equal(expected, response.StatusCode);
            var (user, state) = await StateAsync(id);
            Assert.Equal(before.User.FullName, user.FullName);
            Assert.Equal(before.User.PasswordHash, user.PasswordHash);
            Assert.Equal(1, state.SecurityVersion);
            Assert.Empty(await AuditsAsync(id, "AccountCreated"));
            if (scenario == "self")
            {
                // Ordinary edits of the operator's own row need no admission and still save.
                var own = await GetAsync(host, id); own.FullName = "Synthetic Operator (edited)";
                await PutAsync(host, own);
                Assert.Equal("Synthetic Operator (edited)", (await StateAsync(id)).User.FullName);
            }
        }
        finally
        {
            await using var restore = fixture.CreateContext();
            await restore.Users.Where(x => x.ID == adminID).ExecuteUpdateAsync(x => x.SetProperty(u => u.AccessTree, AdminTree).SetProperty(u => u.FullName, "Synthetic User"));
            await restore.Users.Where(x => x.ID == id).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsProtected, false));
        }
    }

    [Fact]
    public async Task A_fresh_staged_session_is_accepted_as_a_full_proof_operator()
    {
        var points = new List<string>();
        using var host = await HostAsync(legacyToken: false, observe: points.Add);
        var id = await CreateAsync(host, NewUser(requireChange: false, sendVerification: false));
        var dto = await GetAsync(host, id); dto.Password = OtherPassword;
        await PutAsync(host, dto);
        var (user, state) = await StateAsync(id);
        Assert.True(HashService.VerifyVersionedPassword(OtherPassword, user.Salt, user.PasswordHash));
        Assert.Equal(2, state.SecurityVersion);
        Assert.Equal(("AdminPasswordSetRequiringChange", adminID, 2L), Assert.Single(await AuditsAsync(id, "AccountCreated")));
        Assert.Contains("LegacyAdmission", points); Assert.Contains("AdmissionLock", points); Assert.Contains("LegacyMutation", points);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Assign_random_passwords_admits_every_credential_in_one_save_and_skips_protected_rows(bool? requireChange)
    {
        using var host = await HostAsync();
        var first = await CreateAsync(host, NewUser(requireChange: false, sendVerification: false));
        var second = await CreateAsync(host, NewUser(requireChange: false, sendVerification: false));
        var guarded = await CreateAsync(host, NewUser(requireChange: false, sendVerification: false));
        await using (var setup = fixture.CreateContext())
            await setup.Users.Where(x => x.ID == guarded).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsProtected, true));
        var session = Assert.IsType<SessionIssued>(await LoginAsync(host, (await GetAsync(host, first)).Username, Password));
        var query = requireChange is null ? "" : $"?requireChangeAtNextLogin={(requireChange.Value ? "true" : "false")}";
        using var response = await host.Client.PostAsJsonAsync("/api/IdentityUser/AssignRandomPasswords" + query,
            new SelectStateDTO<UserListDTO> { Items = [new() { ID = first.ToString() }, new() { ID = second.ToString() }, new() { ID = guarded.ToString() }] });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var envelope = (await response.Content.ReadFromJsonAsync<ShiftEntityResponse<IEnumerable<UserInfoDTO>>>())!;
        var assigned = System.Text.Json.JsonSerializer.Deserialize<List<UserInfoDTO>>(((System.Text.Json.JsonElement)envelope.Additional!["Users"]).GetRawText(),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal(2, assigned.Count);
        foreach (var info in assigned)
        {
            var (user, state) = await StateAsync(long.Parse(info.ID!));
            Assert.True(HashService.VerifyVersionedPassword(info.PlainTextPassword!, user.Salt, user.PasswordHash));
            // The host configures Security.RequirePasswordChange = true, so no choice means true.
            Assert.Equal(requireChange ?? true, user.RequireChangePassword);
            Assert.Equal(2, state.SecurityVersion);
            Assert.Equal((PasswordAudit(requireChange ?? true), adminID, 2L), Assert.Single(await AuditsAsync(user.ID, "AccountCreated")));
        }
        var (untouched, untouchedState) = await StateAsync(guarded);
        Assert.True(HashService.VerifyVersionedPassword(Password, untouched.Salt, untouched.PasswordHash)); Assert.Equal(1, untouchedState.SecurityVersion);
        Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await RefreshAsync(host, session.Session.RefreshToken)).Code);

        // A length below the shared policy minimum is refused before anything is generated.
        using var tooShort = await host.Client.PostAsJsonAsync("/api/IdentityUser/AssignRandomPasswords?passwordLength=8",
            new SelectStateDTO<UserListDTO> { Items = [new() { ID = first.ToString() }] });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal(2, (await StateAsync(first)).State.SecurityVersion);
    }

    [Fact]
    public async Task Import_records_security_state_admin_provenance_and_the_creation_audit()
    {
        using var host = await HostAsync();
        var username = "synthetic-import-" + Guid.NewGuid().ToString("N")[..12];
        using var response = await host.Client.PostAsJsonAsync("/api/IdentityUser/ImportUsers", new UserImportDTO
        {
            Users = [new UserImportUserDTO { FullName = "Synthetic Import", Username = username, Email = username + "@example.invalid", CompanyBranchID = branchID }]
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        await using var db = fixture.CreateContext();
        var user = await db.Users.SingleAsync(x => x.Username == username);
        var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == user.ID);
        Assert.True(user.RequireChangePassword); Assert.True(user.EmailVerified); Assert.Null(user.VerificationSASToken);
        Assert.Equal(41, user.PasswordHash.Length); Assert.False(VersionedPasswordHash.NeedsUpgrade(user.PasswordHash));
        Assert.Equal(1, state.SecurityVersion); Assert.Equal(username.ToUpperInvariant(), state.UsernameLookupKey);
        Assert.Equal(RecoveryEmailProvenance.TrustedAdminAssignment, state.RecoveryEmailProvenance);
        Assert.True(RecoveryContact.IsEligible(user, state));
        Assert.Equal(("AccountCreated", adminID, 1L), Assert.Single(await AuditsAsync(user.ID)));
        Assert.Empty(host.Verifications.Sent); Assert.Empty(Inbox.Messages);
    }

    [Fact]
    public async Task Verify_phones_marks_the_saved_phone_verified_without_a_version_change()
    {
        using var host = await HostAsync();
        var id = await CreateAsync(host, NewUser(requireChange: false, sendVerification: false, phone: Phone));
        var session = Assert.IsType<SessionIssued>(await LoginAsync(host, (await GetAsync(host, id)).Username, Password));
        using var response = await host.Client.PostAsJsonAsync("/api/IdentityUser/VerifyPhones",
            new SelectStateDTO<UserListDTO> { Items = [new() { ID = id.ToString() }] });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var envelope = (await response.Content.ReadFromJsonAsync<ShiftEntityResponse<IEnumerable<UserListDTO>>>())!;
        Assert.True(Assert.Single(envelope.Entity!).PhoneVerified);
        var (user, state) = await StateAsync(id);
        Assert.True(user.PhoneVerified); Assert.Equal(1, state.SecurityVersion);
        Assert.Equal(("AdminPhoneVerified", adminID, 1L), Assert.Single(await AuditsAsync(id, "AccountCreated")));
        Assert.IsType<SessionIssued>(await RefreshAsync(host, session.Session.RefreshToken));
    }

    [Fact]
    public async Task A_stale_form_save_cannot_overwrite_a_concurrent_self_service_password_change()
    {
        using var setup = await HostAsync();
        var id = await CreateAsync(setup, NewUser(requireChange: false, sendVerification: false));
        var username = (await GetAsync(setup, id)).Username;
        using var gate = new AdmissionGate("LegacyAdmission");
        using var gated = await HostAsync(observe: gate.Observe);
        var dto = await GetAsync(gated, id);
        dto.Password = OtherPassword; dto.FullName = "Synthetic Form User (edited)";
        var pending = gated.Client.PutAsJsonAsync($"/api/IdentityUser/{id}", dto);
        const string userChosen = "User chosen phrase 29!";
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            using var v2 = new IdentityHttpHost(fixture);
            var session = Assert.IsType<SessionIssued>(await IdentityHttpHost.Read(await v2.Client.PostAsJsonAsync("/api/identity/v2/login",
                new PasswordLoginRequest(username, Password, IdentityHttpHost.Pkce().Challenge))));
            var pkce = IdentityHttpHost.Pkce();
            var start = Assert.IsType<ChallengeRequired>(await v2.StartPasswordChangeAsync(session.Session.Token, pkce.Challenge));
            var ready = Assert.IsType<ChallengeRequired>(await v2.ProvePasswordAsync(start.Challenge.Handle!, Password, pkce.Verifier));
            Assert.IsType<PasswordChanged>(await v2.ChangePasswordAsync(ready.Challenge.Handle!, userChosen, pkce.Verifier));
        }
        finally { gate.Release(); }
        using var response = await pending;
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var (user, state) = await StateAsync(id);
        Assert.True(HashService.VerifyVersionedPassword(userChosen, user.Salt, user.PasswordHash));
        Assert.Equal("Synthetic Form User", user.FullName); Assert.Equal(2, state.SecurityVersion);
        Assert.Empty(await AuditsAsync(id, "AccountCreated", "PasswordChangeStarted", "PasswordChangePasswordProven", "PasswordChanged", "SessionIssued"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Form_save_and_staged_refresh_serialize_in_both_lock_orders(bool saveFirst)
    {
        using var setup = await HostAsync();
        var id = await CreateAsync(setup, NewUser(requireChange: false, sendVerification: false));
        var username = (await GetAsync(setup, id)).Username;
        var session = Assert.IsType<SessionIssued>(await LoginAsync(setup, username, Password));
        using var gate = new AdmissionGate("AdmissionLock");
        var signal = new SqlCommandSignal("UPDLOCK");
        using var legacy = await HostAsync(observe: saveFirst ? gate.Observe : (Action<string>?)null, interceptors: saveFirst ? Array.Empty<IInterceptor>() : [signal]);
        using var staged = new IdentityHttpHost(fixture, null, saveFirst ? (Action<string>?)null : gate.Observe, saveFirst ? [signal] : Array.Empty<IInterceptor>());
        var dto = await GetAsync(legacy, id); dto.Password = OtherPassword;
        Task<HttpResponseMessage>? save = null; Task<AuthOutcome>? refresh = null;
        if (saveFirst) save = legacy.Client.PutAsJsonAsync($"/api/IdentityUser/{id}", dto);
        else refresh = staged.RefreshAsync(session.Session.RefreshToken);
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            if (saveFirst) refresh = staged.RefreshAsync(session.Session.RefreshToken);
            else save = legacy.Client.PutAsJsonAsync($"/api/IdentityUser/{id}", dto);
            await signal.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(saveFirst ? refresh!.IsCompleted : save!.IsCompleted);
        }
        finally { gate.Release(); }
        using var response = await save!;
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await refresh!;
        Assert.Equal(2, (await StateAsync(id)).State.SecurityVersion);
        if (saveFirst) Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(result).Code);
        else
        {
            // Refresh admitted first returns only the old version with bounded access; it cannot renew again.
            var old = Assert.IsType<SessionIssued>(result);
            Assert.Equal("1", new JwtSecurityTokenHandler().ReadJwtToken(old.Session.Token).Claims.Single(x => x.Type == "shift_sv").Value);
            Assert.True(old.Session.TokenLifeTimeInSeconds <= 900);
            Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await staged.RefreshAsync(old.Session.RefreshToken)).Code);
        }
    }

    [Fact]
    public async Task Two_form_saves_on_one_user_serialize_and_the_second_reports_a_conflict()
    {
        using var setup = await HostAsync();
        var id = await CreateAsync(setup, NewUser(requireChange: false, sendVerification: false));
        using var gate = new AdmissionGate("AdmissionLock");
        using var first = await HostAsync(observe: gate.Observe);
        var signal = new SqlCommandSignal("UPDLOCK");
        using var second = await HostAsync(interceptors: [signal]);
        var a = await GetAsync(first, id); var b = await GetAsync(second, id);
        var nameA = "Renamed-" + Guid.NewGuid().ToString("N")[..8]; var nameB = "Renamed-" + Guid.NewGuid().ToString("N")[..8];
        a.Username = nameA; b.Username = nameB;
        var saveA = first.Client.PutAsJsonAsync($"/api/IdentityUser/{id}", a);
        Task<HttpResponseMessage>? saveB = null;
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            saveB = second.Client.PutAsJsonAsync($"/api/IdentityUser/{id}", b);
            await signal.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(saveB.IsCompleted);
        }
        finally { gate.Release(); }
        using var responseA = await saveA; using var responseB = await saveB!;
        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, responseB.StatusCode);
        var (user, state) = await StateAsync(id);
        Assert.Equal(nameA, user.Username); Assert.Equal(2, state.SecurityVersion);
        Assert.Single(await AuditsAsync(id, "AccountCreated"));
    }

    [Fact]
    public async Task A_save_failure_after_admission_leaves_nothing_committed()
    {
        using var setup = await HostAsync();
        var id = await CreateAsync(setup, NewUser(requireChange: false, sendVerification: false));
        using var faulty = await HostAsync(interceptors: [new AdmittedSaveFault()]);
        var dto = await GetAsync(faulty, id); dto.Password = OtherPassword; dto.FullName = "Synthetic Form User (edited)";
        HttpResponseMessage? response = null;
        try { response = await faulty.Client.PutAsJsonAsync($"/api/IdentityUser/{id}", dto); }
        catch (Exception error) when (error is not Xunit.Sdk.XunitException) { }
        Assert.True(response is null || response.StatusCode == HttpStatusCode.InternalServerError);
        response?.Dispose();
        var (user, state) = await StateAsync(id);
        Assert.True(HashService.VerifyVersionedPassword(Password, user.Salt, user.PasswordHash));
        Assert.Equal("Synthetic Form User", user.FullName); Assert.Equal(1, state.SecurityVersion);
        Assert.Empty(await AuditsAsync(id, "AccountCreated"));
        // The row is fully usable afterwards.
        dto = await GetAsync(setup, id); dto.Password = OtherPassword;
        await PutAsync(setup, dto);
        Assert.Equal(2, (await StateAsync(id)).State.SecurityVersion);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<LegacyIdentityHttpHost<IdentityTestDbContext>> HostAsync(bool legacyToken = true, Action<string>? observe = null,
        params IInterceptor[] interceptors)
    {
        var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true, observe, interceptors);
        string token;
        if (legacyToken)
        {
            // Mint the pre-cutover credential with production registration, then present it to the staged host.
            using var oldAuthority = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture);
            oldAuthority.Services.GetRequiredService<ShiftIdentityConfiguration>().Token.Issuer = fixture.Options.Issuer;
            using var login = await oldAuthority.Client.PostAsJsonAsync("/api/Auth/Login", new LoginDTO { Username = AdminName, Password = fixture.Password });
            Assert.True(login.StatusCode == HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
            token = (await login.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!.Entity!.Token;
        }
        else token = Assert.IsType<SessionIssued>(await LoginAsync(host, AdminName, fixture.Password)).Session.Token;
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return host;
    }

    private static async Task<AuthOutcome> LoginAsync(LegacyIdentityHttpHost<IdentityTestDbContext> host, string username, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/v2/login")
        { Content = JsonContent.Create(new PasswordLoginRequest(username, password, IdentityHttpHost.Pkce().Challenge)) };
        return await IdentityHttpHost.Read(await host.Client.SendAsync(request));
    }

    private static async Task<AuthOutcome> RefreshAsync(LegacyIdentityHttpHost<IdentityTestDbContext> host, string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/v2/refresh") { Content = JsonContent.Create(new RenewSessionRequest(refreshToken)) };
        return await IdentityHttpHost.Read(await host.Client.SendAsync(request));
    }

    private UserDTO NewUser(string? email = null, bool requireChange = true, bool sendVerification = true, string? phone = null) => new()
    {
        Username = "synthetic-form-" + Guid.NewGuid().ToString("N")[..12],
        FullName = "Synthetic Form User", IsActive = true, AccessTree = "{}",
        CompanyBranchID = new ShiftEntitySelectDTO { Value = branchID },
        Email = email, Phone = phone, Password = Password, RequireChangeAtNextLogin = requireChange, SendVerification = sendVerification
    };

    private static async Task<long> CreateAsync(LegacyIdentityHttpHost<IdentityTestDbContext> host, UserDTO dto)
    {
        using var response = await host.Client.PostAsJsonAsync("/api/IdentityUser", dto);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return long.Parse((await response.Content.ReadFromJsonAsync<ShiftEntityResponse<UserDTO>>())!.Entity!.ID!);
    }

    private static async Task<UserDTO> GetAsync(LegacyIdentityHttpHost<IdentityTestDbContext> host, long id)
    {
        using var response = await host.Client.GetAsync($"/api/IdentityUser/{id}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ShiftEntityResponse<UserDTO>>())!.Entity!;
    }

    private static async Task PutAsync(LegacyIdentityHttpHost<IdentityTestDbContext> host, UserDTO dto)
    {
        using var response = await host.Client.PutAsJsonAsync($"/api/IdentityUser/{dto.ID}", dto);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<(User User, UserSecurityState State)> StateAsync(long id)
    {
        await using var db = fixture.CreateContext();
        return (await db.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.ID == id),
            await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == id));
    }

    private async Task<List<(string Outcome, long ActorUserID, long SecurityVersion)>> AuditsAsync(long id, params string[] except)
    {
        await using var db = fixture.CreateContext();
        // Only operator-attributed rows: the target's own sign-ins and the delivery bookkeeping carry no actor.
        return (await db.Set<AuthenticationAuditEvent>().AsNoTracking().Where(x => x.UserID == id && x.ActorUserID != null && !except.Contains(x.Outcome))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.SecurityVersion).ToListAsync()).Select(x => (x.Outcome, x.ActorUserID ?? 0, x.SecurityVersion)).ToList();
    }

    private static string PasswordAudit(bool requireChange) => requireChange ? "AdminPasswordSetRequiringChange" : "AdminPasswordSet";

    private LocalSecurityInbox Inbox => Assert.IsType<LocalSecurityInbox>(fixture.EmailSink);

    /// <summary>Fails the flush of a save whose admission changed a security row, so the whole transaction rolls back.</summary>
    private sealed class AdmittedSaveFault : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context && context.ChangeTracker.Entries<UserSecurityState>().Any(x => x.State == EntityState.Modified))
                throw new IOException("Synthetic save transport failure.");
            return ValueTask.FromResult(result);
        }
    }
}
