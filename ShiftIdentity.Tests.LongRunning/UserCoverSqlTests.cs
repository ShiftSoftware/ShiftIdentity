using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.TypeAuth.AspNetCore;
using ShiftSoftware.TypeAuth.Core;
using ShiftSoftware.TypeAuth.Core.Actions;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// Review F16: an administrator changes another user's username, password, sign-in, email, phone or status only when their
/// rights cover everything that user holds, their own tree and every assigned tree (deleted ones included). Covered
/// on both paths: the staged authority (the form, delete and Assign Random Passwords admitted in the save, and the
/// v2 administrator routes) and the direct writes of a host without it (the form, delete, Assign Random Passwords and
/// Reset TOTP). An ordinary edit of a user the administrator doesn't cover still saves.
/// </summary>
[Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class UserCoverSqlTests(SqlIdentityFixture fixture) : IClassFixture<SqlIdentityFixture>, IAsyncLifetime
{
    private const string AdminName = "synthetic-cover-admin";
    // Users, MFA recovery and Access Trees read, with wildcard data-level access on every dimension the User row carries.
    private const string AdminTree = "{\"ShiftIdentityActions\":{\"Users\":[\"r\",\"w\",\"d\"],\"ManageMfaRecovery\":[\"m\"],\"AccessTrees\":[\"r\"],\"DataLevelAccess\":{\"Countries\":[\"r\",\"w\",\"d\"],\"Regions\":[\"r\",\"w\",\"d\"],\"Companies\":[\"r\",\"w\",\"d\"],\"Branches\":[\"r\",\"w\",\"d\"]}}}";
    // Within the administrator: they hold Users read.
    private const string CoveredTree = "{\"ShiftIdentityActions\":{\"Users\":[\"r\"]}}";
    // Beyond the administrator, who can't assign it: Access Trees write.
    private const string BeyondTree = "{\"ShiftIdentityActions\":{\"AccessTrees\":[\"r\",\"w\"]}}";
    // Two trees within the administrator that together grant what one tree can't hold: a wildcard on Regions, and the
    // self row of Regions beyond it.
    private const string OverlappingWildcard = "{\"ShiftIdentityActions\":{\"DataLevelAccess\":{\"Regions\":[\"r\",\"w\"]}}}";
    private const string OverlappingSelfRow = "{\"ShiftIdentityActions\":{\"DataLevelAccess\":{\"Regions\":{\"_shift_software_type_auth_core_self_reference\":[\"r\",\"w\",\"d\"]}}}}";
    private const string NewPassword = "Cover chosen phrase 63!";
    private static string Phone => "+96477" + Random.Shared.Next(10000000, 99999999).ToString(System.Globalization.CultureInfo.InvariantCulture);
    private ControlledClock clock = null!;

    public async ValueTask InitializeAsync()
    {
        clock = new(DateTimeOffset.UtcNow); fixture.Clock = clock;
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var adminID = await db.Users.IgnoreQueryFilters().Where(x => x.Username == AdminName).Select(x => x.ID).SingleOrDefaultAsync();
        if (adminID == 0) adminID = await fixture.CreateSyntheticUserAsync(AdminName, AdminTree);
        await db.Users.IgnoreQueryFilters().Where(x => x.ID == adminID).ExecuteUpdateAsync(x => x
            .SetProperty(u => u.IsActive, true).SetProperty(u => u.IsDeleted, false).SetProperty(u => u.AccessTree, AdminTree));
        await db.Set<UserSecurityState>().Where(x => x.UserID == adminID).ExecuteUpdateAsync(x => x
            .SetProperty(s => s.SecurityVersion, 1).SetProperty(s => s.FailedProofs, 0).SetProperty(s => s.FailureWindowStart, (DateTimeOffset?)null));
    }

    public ValueTask DisposeAsync() { fixture.Clock = TimeProvider.System; return ValueTask.CompletedTask; }

    // ── The user form ────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(FormChanges))]
    public async Task A_form_change_to_credentials_contact_or_status_needs_the_admin_to_cover_the_user(bool authority, string change, bool beyond)
    {
        using var host = await HostAsync(authority);
        var (id, username) = await TargetAsync(beyond ? BeyondTree : CoveredTree);
        var before = await RowAsync(id);
        var dto = await GetAsync(host, id);
        Apply(dto, change);
        using var response = await host.Client.PutAsJsonAsync($"/api/IdentityUser/{id}", dto);
        if (beyond)
        {
            AssertRefused(await RefusedAsync(response), AdministratorCover.Message, username);
            Assert.Equal(before, await RowAsync(id));
        }
        else
        {
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            Assert.NotEqual(before, await RowAsync(id));
        }
    }

    public static TheoryData<bool, string, bool> FormChanges()
    {
        var data = new TheoryData<bool, string, bool>();
        foreach (var authority in new[] { true, false })
            foreach (var change in new[] { "username", "password", "email", "phone", "deactivate", "device" })
                foreach (var beyond in new[] { true, false })
                    data.Add(authority, change, beyond);
        return data;
    }

    // Everything else stays editable: the name and birth date here, with the password, email, phone and status
    // submitted unchanged.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_ordinary_edit_of_a_user_the_admin_does_not_cover_saves(bool authority)
    {
        using var host = await HostAsync(authority);
        var (id, _) = await TargetAsync(BeyondTree, email: $"cover-{Guid.NewGuid():N}@example.invalid");
        var dto = await GetAsync(host, id);
        dto.FullName = "Synthetic Cover User (edited)"; dto.BirthDate = new DateTime(1990, 1, 2);
        using var response = await host.Client.PutAsJsonAsync($"/api/IdentityUser/{id}", dto);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var row = await RowAsync(id);
        Assert.Equal("Synthetic Cover User (edited)", row.FullName);
    }

    // What the user holds is their own tree and every assigned tree, a deleted tree included: sign-in still grants it.
    [Theory]
    [InlineData(true, "own")]
    [InlineData(true, "deleted")]
    [InlineData(false, "own")]
    [InlineData(false, "deleted")]
    public async Task The_users_own_tree_and_deleted_trees_count(bool authority, string where)
    {
        using var host = await HostAsync(authority);
        var (id, username) = where == "own" ? await TargetAsync(null, own: BeyondTree) : await TargetAsync(BeyondTree, deletedTree: true);
        var before = await RowAsync(id);
        var dto = await GetAsync(host, id);
        Apply(dto, "password");
        using var response = await host.Client.PutAsJsonAsync($"/api/IdentityUser/{id}", dto);
        AssertRefused(await RefusedAsync(response), AdministratorCover.Message, username);
        Assert.Equal(before, await RowAsync(id));
    }

    // The admin covers a user when they cover each of the user's trees. One tree's wildcard on a dynamic action and
    // another's self row beyond it can't be written as one tree; reduced as one, they refused every admin, as TCA's
    // "System: Basic" and "Data by Branch: Self" did for 194 of 277 users on the 23 October backup.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Trees_that_overlap_are_covered_by_an_admin_who_covers_each(bool authority)
    {
        using var host = await HostAsync(authority);
        var (id, _) = await TargetAsync(OverlappingSelfRow, own: OverlappingWildcard);
        var before = await RowAsync(id);
        var dto = await GetAsync(host, id);
        Apply(dto, "password");
        using var response = await host.Client.PutAsJsonAsync($"/api/IdentityUser/{id}", dto);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.NotEqual(before.PasswordHash, (await RowAsync(id)).PasswordHash);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Deleting_needs_the_admin_to_cover_the_user(bool authority, bool beyond)
    {
        using var host = await HostAsync(authority);
        var (id, username) = await TargetAsync(beyond ? BeyondTree : CoveredTree);
        using var response = await host.Client.DeleteAsync($"/api/IdentityUser/{id}");
        if (beyond) AssertRefused(await RefusedAsync(response), AdministratorCover.Message, username);
        else Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.Equal(!beyond, (await RowAsync(id)).IsDeleted);
    }

    // ── Bulk actions ─────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Assign_random_passwords_with_one_user_not_covered_changes_nobody(bool authority)
    {
        using var host = await HostAsync(authority);
        var (covered, _) = await TargetAsync(CoveredTree);
        var (beyond, beyondName) = await TargetAsync(BeyondTree);
        var before = (await RowAsync(covered), await RowAsync(beyond));
        using var response = await host.Client.PostAsJsonAsync("/api/IdentityUser/AssignRandomPasswords",
            new SelectStateDTO<UserListDTO> { Items = [new() { ID = covered.ToString() }, new() { ID = beyond.ToString() }] });
        AssertRefused(await RefusedAsync<IEnumerable<UserInfoDTO>>(response), AdministratorCover.BulkMessage, beyondName);
        Assert.Equal(before, (await RowAsync(covered), await RowAsync(beyond)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Assign_random_passwords_for_covered_users_goes_ahead(bool authority)
    {
        using var host = await HostAsync(authority);
        var (covered, _) = await TargetAsync(CoveredTree);
        var before = await RowAsync(covered);
        using var response = await host.Client.PostAsJsonAsync("/api/IdentityUser/AssignRandomPasswords",
            new SelectStateDTO<UserListDTO> { Items = [new() { ID = covered.ToString() }] });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.NotEqual(before.PasswordHash, (await RowAsync(covered)).PasswordHash);
    }

    // Reset TOTP is a direct write of hosts without the authority (with it the route refuses and changes nothing).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reset_totp_needs_the_admin_to_cover_every_selected_user(bool beyond)
    {
        using var host = await HostAsync(authority: false);
        var (covered, _) = await TargetAsync(CoveredTree);
        var (other, otherName) = await TargetAsync(beyond ? BeyondTree : CoveredTree);
        await using (var db = fixture.CreateContext())
            await db.Users.Where(x => x.ID == covered || x.ID == other).ExecuteUpdateAsync(x => x.SetProperty(u => u.TotpSecret, new byte[] { 1, 2, 3 }));
        using var response = await host.Client.PostAsJsonAsync("/api/IdentityUser/ResetTotp",
            new SelectStateDTO<UserListDTO> { Items = [new() { ID = covered.ToString() }, new() { ID = other.ToString() }] });
        if (beyond) AssertRefused(await RefusedAsync<IEnumerable<UserInfoDTO>>(response), AdministratorCover.BulkMessage, otherName);
        else Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        await using var verify = fixture.CreateContext();
        Assert.All(await verify.Users.Where(x => x.ID == covered || x.ID == other).Select(x => x.TotpSecret).ToListAsync(),
            secret => Assert.Equal(beyond, secret is not null));
    }

    // ── The authority's v2 administrator routes ──────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(RouteChanges))]
    public async Task An_administrator_route_needs_the_admin_to_cover_the_user(string route, bool beyond)
    {
        using var host = new IdentityHttpHost(fixture);
        var (id, _) = await TargetAsync(beyond ? BeyondTree : CoveredTree, email: $"cover-{Guid.NewGuid():N}@example.invalid");
        var before = await RowAsync(id);
        var version = await VersionAsync(id);
        var admin = Assert.IsType<SessionIssued>(await IdentityHttpHost.Read(await host.Client.PostAsJsonAsync("/api/identity/v2/login",
            new PasswordLoginRequest(AdminName, fixture.Password, IdentityHttpHost.Pkce().Challenge)))).Session.Token;
        var outcome = await RouteAsync(host, admin, route, id);
        if (beyond)
        {
            var refused = Assert.IsType<AuthenticationRefused>(outcome);
            Assert.Equal(AuthenticationFailure.AccessNotCovered, refused.Code);
            var missing = Assert.Single(refused.Uncovered!);
            Assert.Equal("Access Trees", missing.Action);
            Assert.Equal(before, await RowAsync(id));
            Assert.Equal(version, await VersionAsync(id));
            await using var db = fixture.CreateContext();
            Assert.False(await db.Set<AuthenticationOperation>().AnyAsync(x => x.UserID == id));
        }
        else Assert.IsNotType<AuthenticationRefused>(outcome);
    }

    public static TheoryData<string, bool> RouteChanges()
    {
        var data = new TheoryData<string, bool>();
        foreach (var route in new[] { "username", "password", "email", "deactivate", "mfa-recovery", "mfa-turn-off", "manual-reset" })
            foreach (var beyond in new[] { true, false })
                data.Add(route, beyond);
        return data;
    }

    // Not in the rule: reactivating and mailing a reset link to the user's own address need no cover.
    [Theory]
    [InlineData("activate")]
    [InlineData("email-reset")]
    public async Task Other_administrator_routes_do_not_need_cover(string route)
    {
        using var host = new IdentityHttpHost(fixture);
        var (id, _) = await TargetAsync(BeyondTree, email: $"cover-{Guid.NewGuid():N}@example.invalid");
        if (route == "activate")
            await using (var db = fixture.CreateContext())
                await db.Users.Where(x => x.ID == id).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, false));
        var admin = Assert.IsType<SessionIssued>(await IdentityHttpHost.Read(await host.Client.PostAsJsonAsync("/api/identity/v2/login",
            new PasswordLoginRequest(AdminName, fixture.Password, IdentityHttpHost.Pkce().Challenge)))).Session.Token;
        Assert.IsNotType<AuthenticationRefused>(await RouteAsync(host, admin, route, id));
    }

    private static async Task<AuthOutcome> RouteAsync(IdentityHttpHost host, string admin, string route, long id)
    {
        async Task<AuthOutcome> Send<T>(string path, T body)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/v2/" + path) { Content = JsonContent.Create(body) };
            request.Headers.Authorization = new("Bearer", admin);
            return await IdentityHttpHost.Read(await host.Client.SendAsync(request));
        }
        return route switch
        {
            "password" => await host.AdminSetPasswordAsync(admin, id, NewPassword, requireChange: false),
            "email" => await host.AdminChangeEmailAsync(admin, id, $"cover-{Guid.NewGuid():N}@example.invalid", sendVerification: false),
            "deactivate" => await host.AdminSetActiveAsync(admin, id, false),
            "activate" => await host.AdminSetActiveAsync(admin, id, true),
            "username" => await host.AdminChangeUsernameAsync(admin, id, "synthetic-cover-renamed-" + Guid.NewGuid().ToString("N")[..8]),
            "mfa-recovery" => await host.IssueRecoveryAsync(admin, id, "Synthetic cover check"),
            "mfa-turn-off" => await host.TurnOffAccountMfaAsync(admin, id, "Synthetic cover check"),
            "manual-reset" => await Send("password-reset/admin", new AdminPasswordResetRequest(id, true)),
            "email-reset" => await Send("password-reset/admin", new AdminPasswordResetRequest(id, false)),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };
    }

    // ── What the check reads ─────────────────────────────────────────────────────────────────────────────────────

    // The check reads exactly the trees sign-in grants: both issuers put the user's own tree first, then every
    // assigned tree, a deleted one included, and the check's database read finds the same.
    [Fact]
    public async Task Both_sign_in_issuers_grant_the_trees_the_check_reads()
    {
        const string own = "{\"ShiftIdentityActions\":{\"Users\":[\"r\"]}}";
        const string assigned = "{\"ShiftIdentityActions\":{\"Teams\":[\"r\"]}}";
        const string deleted = "{\"ShiftIdentityActions\":{\"Brands\":[\"r\"]}}";
        var (id, username) = await TargetAsync(assigned, own: own);
        await AssignTreeAsync(id, deleted, isDeleted: true);
        string[] expected = [own, assigned, deleted];

        await using (var db = fixture.CreateContext())
        {
            var read = (await UserCover.HeldTreesAsync(db, [id]))[id];
            Assert.Equal(own, read[0]);
            Assert.Equal(expected.Order(), read.Order());
        }

        // The deployed issuer of a host without the authority.
        using var legacy = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture);
        using var login = await legacy.Client.PostAsJsonAsync("/api/Auth/Login", new LoginDTO { Username = username, Password = fixture.Password });
        Assert.True(login.StatusCode == HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        var legacyToken = (await login.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!.Entity!.Token;

        // The authority's issuer.
        using var host = new IdentityHttpHost(fixture);
        var v2Token = Assert.IsType<SessionIssued>(await IdentityHttpHost.Read(await host.Client.PostAsJsonAsync("/api/identity/v2/login",
            new PasswordLoginRequest(username, fixture.Password, IdentityHttpHost.Pkce().Challenge)))).Session.Token;

        foreach (var token in new[] { legacyToken, v2Token })
        {
            var granted = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
                .Where(x => x.Type == TypeAuthClaimTypes.AccessTree).Select(x => x.Value).ToList();
            Assert.Equal(own, granted[0]);
            Assert.Equal(expected.Order(), granted.Order());
        }
    }

    // Effective Permissions lists the same trees, each as it is: one tree can't hold everything several grant together.
    [Fact]
    public async Task Effective_permissions_lists_the_trees_the_check_reads()
    {
        var (id, _) = await TargetAsync(OverlappingSelfRow, own: OverlappingWildcard);
        using var host = await HostAsync(false);

        using var response = await host.Client.GetAsync($"/api/IdentityUser/{id}/EffectivePermissions");

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var permissions = (await response.Content.ReadFromJsonAsync<ShiftEntityResponse<UserEffectivePermissionsDTO>>())!.Entity!;
        Assert.Equal([OverlappingWildcard, OverlappingSelfRow], permissions.AccessTrees);
    }

    // A host registers its own action trees beside Identity's. The authority builds the operator's rights over all of
    // them, so a grant in the host's tree counts: built over Identity's tree alone, the target below would look covered.
    [Theory]
    [InlineData("route", false)]
    [InlineData("route", true)]
    [InlineData("form", false)]
    [InlineData("form", true)]
    public async Task The_authority_counts_every_action_tree_the_host_registered(string path, bool adminHoldsIt)
    {
        const string hostGrant = "\"SyntheticHostActions\":{\"Reports\":[\"r\",\"w\"]}";
        if (adminHoldsIt)
            await using (var db = fixture.CreateContext())
                await db.Users.Where(x => x.Username == AdminName).ExecuteUpdateAsync(x => x.SetProperty(u => u.AccessTree, AdminTree[..^1] + "," + hostGrant + "}"));
        var (id, username) = await TargetAsync("{" + hostGrant + "}");
        var before = await RowAsync(id);

        // The public registration, as a deployed host builds it, with one more action tree.
        using var host = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture,
            configureServices: services => services.Configure<TypeAuthAspNetCoreOptions>(o => o.AddActionTree<SyntheticHostActions>()));
        var admin = Assert.IsType<SessionIssued>(await IdentityHttpHost.Read(await host.Client.PostAsJsonAsync("/api/identity/v2/login",
            new PasswordLoginRequest(AdminName, fixture.Password, IdentityHttpHost.Pkce().Challenge)))).Session.Token;
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin);

        if (path == "route")
        {
            var outcome = await IdentityHttpHost.Read(await host.Client.PostAsJsonAsync("/api/identity/v2/admin/password",
                new AdminSetPasswordRequest(id, NewPassword, false)));
            if (adminHoldsIt) Assert.IsType<AdminAccountChanged>(outcome);
            else
            {
                var refused = Assert.IsType<AuthenticationRefused>(outcome);
                Assert.Equal(AuthenticationFailure.AccessNotCovered, refused.Code);
                Assert.Equal("Synthetic Reports", Assert.Single(refused.Uncovered!).Action);
            }
        }
        else
        {
            using var read = await host.Client.GetAsync($"/api/IdentityUser/{id}");
            Assert.True(read.StatusCode == HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
            var dto = (await read.Content.ReadFromJsonAsync<ShiftEntityResponse<UserDTO>>())!.Entity!;
            Apply(dto, "password");
            using var response = await host.Client.PutAsJsonAsync($"/api/IdentityUser/{id}", dto);
            if (adminHoldsIt) Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            else AssertRefused(await RefusedAsync(response), AdministratorCover.Message, username, "Synthetic Reports");
        }

        Assert.Equal(adminHoldsIt, before.PasswordHash != (await RowAsync(id)).PasswordHash);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────────

    private static void Apply(UserDTO dto, string change)
    {
        dto.SendVerification = false;
        switch (change)
        {
            case "username": dto.Username = "synthetic-cover-renamed-" + Guid.NewGuid().ToString("N")[..8]; break;
            case "password": dto.Password = NewPassword; dto.RequireChangeAtNextLogin = false; break;
            case "email": dto.Email = $"cover-{Guid.NewGuid():N}@example.invalid"; break;
            case "phone": dto.Phone = Phone; break;
            case "deactivate": dto.IsActive = false; break;
            case "device": dto.AllowDeviceSignIn = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    // A target in the template's branch, with its own tree and one assigned tree (deleted when asked).
    private async Task<(long ID, string Username)> TargetAsync(string? assigned, string? own = null, bool deletedTree = false, string? email = null)
    {
        var username = "synthetic-cover-" + Guid.NewGuid().ToString("N")[..12];
        var id = await fixture.CreateSyntheticUserAsync(username, own, email: email);
        if (assigned is not null)
        {
            await using var db = fixture.CreateContext();
            var tree = new AccessTree { Name = "Synthetic cover " + Guid.NewGuid().ToString("N")[..8], Tree = assigned, IsDeleted = deletedTree };
            db.AccessTrees.Add(tree); await db.SaveChangesAsync();
            db.UserAccessTrees.Add(new UserAccessTree { UserID = id, AccessTreeID = tree.ID }); await db.SaveChangesAsync();
        }
        return (id, username);
    }

    private async Task AssignTreeAsync(long userID, string tree, bool isDeleted)
    {
        await using var db = fixture.CreateContext();
        var entity = new AccessTree { Name = "Synthetic cover " + Guid.NewGuid().ToString("N")[..8], Tree = tree, IsDeleted = isDeleted };
        db.AccessTrees.Add(entity); await db.SaveChangesAsync();
        db.UserAccessTrees.Add(new UserAccessTree { UserID = userID, AccessTreeID = entity.ID }); await db.SaveChangesAsync();
    }

    private async Task<LegacyIdentityHttpHost<IdentityTestDbContext>> HostAsync(bool authority)
    {
        var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority);
        // The pre-cutover credential, minted with production registration. The staged host is presented one minted
        // for its issuer; a host without the authority signs in itself.
        using var minter = authority ? new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture) : null;
        if (minter is not null) minter.Services.GetRequiredService<ShiftIdentityConfiguration>().Token.Issuer = fixture.Options.Issuer;
        using var login = await (minter ?? host).Client.PostAsJsonAsync("/api/Auth/Login", new LoginDTO { Username = AdminName, Password = fixture.Password });
        Assert.True(login.StatusCode == HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        var token = (await login.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!.Entity!.Token;
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return host;
    }

    private static async Task<UserDTO> GetAsync(LegacyIdentityHttpHost<IdentityTestDbContext> host, long id)
    {
        using var response = await host.Client.GetAsync($"/api/IdentityUser/{id}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ShiftEntityResponse<UserDTO>>())!.Entity!;
    }

    private sealed record Row(string Username, string FullName, string? Email, string? Phone, bool IsActive,
        bool AllowDeviceSignIn, bool IsDeleted, string PasswordHash);

    private async Task<Row> RowAsync(long id)
    {
        await using var db = fixture.CreateContext();
        var user = await db.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.ID == id);
        return new(user.Username, user.FullName, user.Email, user.Phone, user.IsActive, user.AllowDeviceSignIn, user.IsDeleted,
            Convert.ToBase64String(user.PasswordHash));
    }

    private async Task<long> VersionAsync(long id)
    {
        await using var db = fixture.CreateContext();
        return await db.Set<UserSecurityState>().Where(x => x.UserID == id).Select(x => x.SecurityVersion).SingleAsync();
    }

    private static Task<Message> RefusedAsync(HttpResponseMessage response) => RefusedAsync<UserDTO>(response);

    private static async Task<Message> RefusedAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ShiftEntityResponse<T>>())!.Message!;
    }

    // Shaped as the assignment check's refusal: the user, and under it what the admin lacks, here Write on Access Trees.
    private static void AssertRefused(Message message, string body, string username, string missing = "Access Trees")
    {
        Assert.Equal("Error", message.Title);
        Assert.Equal(body, message.Body);
        var user = Assert.Single(message.SubMessages!);
        Assert.Equal(username, user.Title);
        var action = Assert.Single(user.SubMessages!);
        Assert.Equal(missing, action.Title);
        Assert.False(string.IsNullOrWhiteSpace(action.Body));
    }
}

/// <summary>An action tree a host registers beside Identity's.</summary>
[ActionTree("Synthetic host", "Reports of a host")]
public class SyntheticHostActions
{
    public readonly static ReadWriteDeleteAction Reports = new ReadWriteDeleteAction("Synthetic Reports");
}
