using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// The production User form's two checkboxes on the legacy api/IdentityUser write path: the per-save forced password
/// change and the verification link for a new address. Real dashboard registration and routes against an owned
/// fixture database; a recording ISendEmailVerification provider stands in for the mail host.
/// </summary>
[Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class UserFormLegacySqlTests(SqlIdentityFixture fixture) : IClassFixture<SqlIdentityFixture>, IAsyncLifetime
{
    private const string AdminName = "synthetic-form-admin";
    // Users read/write for the routes, plus wildcard data-level write on every dimension the User row carries.
    private const string AdminTree = "{\"ShiftIdentityActions\":{\"Users\":[\"r\",\"w\"],\"AccessTrees\":[\"r\"],\"DataLevelAccess\":{\"Countries\":[\"r\",\"w\"],\"Regions\":[\"r\",\"w\"],\"Companies\":[\"r\",\"w\"],\"Branches\":[\"r\",\"w\"]}}}";
    private const string Password = "Form chosen phrase 61!";
    private const string OtherPassword = "Another chosen phrase 62!";
    private string branchID = "";

    public async ValueTask InitializeAsync()
    {
        await using var db = fixture.CreateContext();
        var template = await db.Users.AsNoTracking().SingleAsync(x => x.ID == fixture.UserID);
        branchID = template.CompanyBranchID!.Value.ToString();
        if (await db.Users.AnyAsync(x => x.Username == AdminName)) return;
        // The legacy login verifies the HMAC hash format, so the operator is created with it directly.
        var hash = HashService.GenerateHash(fixture.Password);
        db.Users.Add(new User
        {
            Username = AdminName, FullName = "Synthetic Operator", IsActive = true, PasswordHash = hash.PasswordHash, Salt = hash.Salt,
            CompanyID = template.CompanyID, CompanyBranchID = template.CompanyBranchID, CountryID = template.CountryID,
            RegionID = template.RegionID, AccessTree = AdminTree
        });
        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_hashes_the_password_and_applies_the_next_login_choice(bool requireChange)
    {
        using var host = await HostAsync();
        var id = await CreateAsync(host, NewUser(requireChange: requireChange));
        await using var db = fixture.CreateContext();
        var user = await db.Users.SingleAsync(x => x.ID == id);
        Assert.Equal(requireChange, user.RequireChangePassword);
        Assert.True(HashService.VerifyPassword(Password, user.Salt, user.PasswordHash));
        Assert.Empty(host.Verifications.Sent);
    }

    [Theory]
    [InlineData("password", true)]
    [InlineData("password", false)]
    [InlineData("none", true)]
    [InlineData("none", false)]
    public async Task Update_changes_the_forced_change_flag_only_when_a_password_is_supplied(string password, bool requireChange)
    {
        using var host = await HostAsync();
        var id = await CreateAsync(host, NewUser(requireChange: !requireChange, sendVerification: false));
        var dto = await GetAsync(host, id);
        // The edit form opens with both choices checked, whatever was chosen before.
        Assert.True(dto.RequireChangeAtNextLogin); Assert.True(dto.SendVerification); Assert.Null(dto.Password);
        dto.Password = password == "password" ? OtherPassword : null;
        dto.RequireChangeAtNextLogin = requireChange;
        await PutAsync(host, dto);
        await using var db = fixture.CreateContext();
        var user = await db.Users.SingleAsync(x => x.ID == id);
        // With a password the choice applies; without one the saved flag (the opposite value) is untouched.
        Assert.Equal(password == "password" ? requireChange : !requireChange, user.RequireChangePassword);
        Assert.Equal(password == "password", HashService.VerifyPassword(OtherPassword, user.Salt, user.PasswordHash));
        Assert.Empty(host.Verifications.Sent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_with_an_email_sends_exactly_one_verification_link_only_when_requested(bool send)
    {
        using var host = await HostAsync();
        var dto = NewUser(email: $"created-{Guid.NewGuid():N}@example.invalid", sendVerification: send);
        var id = await CreateAsync(host, dto);
        await using var db = fixture.CreateContext();
        var user = await db.Users.SingleAsync(x => x.ID == id);
        Assert.False(user.EmailVerified);
        if (!send)
        {
            Assert.Empty(host.Verifications.Sent); Assert.Null(user.VerificationSASToken);
            return;
        }
        var (url, recipient) = Assert.Single(host.Verifications.Sent);
        Assert.Equal(dto.Email, recipient.Email); Assert.Equal(dto.Username, recipient.Username);
        Assert.Contains($"/api/UserManager/VerifyEmail/{id}?expires=", url);
        Assert.Equal(Regex.Match(url, "[?&]token=([^&]+)").Groups[1].Value, user.VerificationSASToken);
        // It is the existing link: opening it verifies the address through the legacy VerifyEmail route.
        using var verify = await host.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.Contains("verified successfully", await verify.Content.ReadAsStringAsync());
        await db.Entry(user).ReloadAsync();
        Assert.True(user.EmailVerified); Assert.Null(user.VerificationSASToken);
    }

    [Theory]
    [InlineData("changed", true, 1)]
    [InlineData("changed", false, 0)]
    [InlineData("unchanged", true, 0)]
    [InlineData("removed", true, 0)]
    public async Task Update_sends_a_verification_link_only_for_a_changed_address_when_requested(string change, bool send, int expectedSends)
    {
        using var host = await HostAsync();
        var id = await CreateAsync(host, NewUser(email: $"saved-{Guid.NewGuid():N}@example.invalid", sendVerification: false));
        await using (var setup = fixture.CreateContext())
            await setup.Users.Where(x => x.ID == id).ExecuteUpdateAsync(x => x.SetProperty(u => u.EmailVerified, true));
        var dto = await GetAsync(host, id);
        dto.Email = change switch { "changed" => $"changed-{Guid.NewGuid():N}@example.invalid", "removed" => null, _ => dto.Email };
        dto.SendVerification = send;
        await PutAsync(host, dto);
        await using var db = fixture.CreateContext();
        var user = await db.Users.SingleAsync(x => x.ID == id);
        Assert.Equal(expectedSends, host.Verifications.Sent.Count);
        Assert.Equal(change == "unchanged", user.EmailVerified);
        Assert.Equal(dto.Email, user.Email);
        if (expectedSends == 1)
        {
            Assert.Equal(dto.Email, Assert.Single(host.Verifications.Sent).User.Email);
            Assert.NotNull(user.VerificationSASToken);
        }
        else Assert.Null(user.VerificationSASToken);
    }

    [Fact]
    public async Task A_failing_mail_provider_never_fails_the_save()
    {
        using var host = await HostAsync();
        host.Verifications.Throw = true;
        var dto = NewUser(email: $"failing-{Guid.NewGuid():N}@example.invalid");
        var id = await CreateAsync(host, dto);
        Assert.Single(host.Verifications.Sent);
        await using var db = fixture.CreateContext();
        var user = await db.Users.SingleAsync(x => x.ID == id);
        Assert.Equal(dto.Email, user.Email); Assert.False(user.EmailVerified); Assert.NotNull(user.VerificationSASToken);
        // The saved row is fully usable: a later edit succeeds and, unchanged, sends nothing more.
        var saved = await GetAsync(host, id);
        saved.FullName = "Synthetic Form User (edited)";
        await PutAsync(host, saved);
        Assert.Single(host.Verifications.Sent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Assign_random_passwords_uses_the_request_choice_or_the_configured_default(bool? requireChange)
    {
        using var host = await HostAsync();
        var id = await CreateAsync(host, NewUser(requireChange: false, sendVerification: false));
        var query = requireChange is null ? "" : $"?requireChangeAtNextLogin={(requireChange.Value ? "true" : "false")}";
        using var response = await host.Client.PostAsJsonAsync("/api/IdentityUser/AssignRandomPasswords" + query,
            new SelectStateDTO<UserListDTO> { Items = [new UserListDTO { ID = id.ToString() }] });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        await using var db = fixture.CreateContext();
        var user = await db.Users.SingleAsync(x => x.ID == id);
        // The host configures Security.RequirePasswordChange = true, so no choice means true.
        Assert.Equal(requireChange ?? true, user.RequireChangePassword);
        Assert.False(HashService.VerifyPassword(Password, user.Salt, user.PasswordHash));
    }

    [Fact]
    public async Task Import_keeps_the_configured_forced_change_and_sends_no_verification()
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
        Assert.True(user.RequireChangePassword); Assert.True(user.EmailVerified); Assert.Null(user.VerificationSASToken);
        Assert.Empty(host.Verifications.Sent);
    }

    private async Task<LegacyIdentityHttpHost<IdentityTestDbContext>> HostAsync()
    {
        var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture);
        using var login = await host.Client.PostAsJsonAsync("/api/Auth/Login", new LoginDTO { Username = AdminName, Password = fixture.Password });
        Assert.True(login.StatusCode == HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        var token = (await login.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!.Entity!.Token;
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return host;
    }

    private UserDTO NewUser(string? email = null, bool requireChange = true, bool sendVerification = true) => new()
    {
        Username = "synthetic-form-" + Guid.NewGuid().ToString("N")[..12],
        FullName = "Synthetic Form User", IsActive = true, AccessTree = "{}",
        CompanyBranchID = new ShiftEntitySelectDTO { Value = branchID },
        Email = email, Password = Password, RequireChangeAtNextLogin = requireChange, SendVerification = sendVerification
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
}
