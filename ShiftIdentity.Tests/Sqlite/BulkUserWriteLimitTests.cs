using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.TypeAuth.AspNetCore;
using Xunit;

namespace ShiftIdentity.Tests.Sqlite;

/// <summary>
/// The user list's bulk actions over HTTP, with and without the authority: an administrator changes or mails at most
/// GeneralActionTree.DataGridBulkWriteMaxTop users in one request. A bigger request is refused as a whole, and nobody is
/// changed or mailed.
/// </summary>
[Trait("Category", "Http")]
public sealed class BulkUserWriteLimitTests(SqliteIdentityFixture fixture) : IClassFixture<SqliteIdentityFixture>, IAsyncLifetime
{
    private const string AssignRandomPasswords = nameof(AssignRandomPasswords);
    private const string ResetTotp = nameof(ResetTotp);
    private const string VerifyEmails = nameof(VerifyEmails);
    private const string VerifyPhones = nameof(VerifyPhones);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Names are unique per test so the class fixture's other users never match.
    private readonly string run = Guid.NewGuid().ToString("N")[..8];

    public async ValueTask InitializeAsync()
    {
        fixture.Clock = TimeProvider.System;
        await fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Every bulk route of the user list. With the authority, Reset TOTP refuses every request before it loads anyone.
    public static TheoryData<string, bool> Routes => new()
    {
        { AssignRandomPasswords, false }, { AssignRandomPasswords, true },
        { VerifyEmails, false }, { VerifyEmails, true },
        { VerifyPhones, false }, { VerifyPhones, true },
        { ResetTotp, false },
    };

    // One host per route, because starting one is most of the time these take.
    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Over_the_limit_nobody_is_changed_and_at_the_limit_every_selected_user_is(string route, bool authority)
    {
        using var host = await HostAsync(authority, limit: 2);
        var ids = await UsersAsync(3);
        var before = await StatesAsync(ids);

        using (var refused = await PostAsync(host, route, Selected(ids)))
            await AssertRefusedAsync(refused, users: 3, limit: 2);

        // "All rows" of a filter, with their number understated, which SelectStateDTO.Count would repeat.
        using (var refused = await PostAsync(host, route, new { All = true, Items = Array.Empty<object>(), Total = 1, Filter = $"startswith(Username,'{run}-user')" }))
            await AssertRefusedAsync(refused, users: 3, limit: 2);

        Assert.Equal(before, await StatesAsync(ids));
        Assert.Empty(host.Verifications.Sent);
        Assert.Equal(0, ((LocalSecurityInbox)fixture.EmailSink!).DeliveryCalls);

        using var response = await PostAsync(host, route, Selected(ids.Take(2)));

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var after = await StatesAsync(ids);
        Assert.All(ids.Take(2), id => Assert.NotEqual(before[id], after[id]));
        Assert.Equal(before[ids[2]], after[ids[2]]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_administrator_without_the_grant_is_held_to_5_users(bool treeRegistered)
    {
        using var host = await HostAsync(authority: false, limit: null, registerTree: treeRegistered);
        var ids = await UsersAsync(6);

        using (var refused = await PostAsync(host, VerifyPhones, Selected(ids)))
            await AssertRefusedAsync(refused, users: 6, limit: 5);

        using var verified = await PostAsync(host, VerifyPhones, Selected(ids.Take(5)));
        Assert.True(verified.StatusCode == HttpStatusCode.OK, await verified.Content.ReadAsStringAsync(Ct));
        await using var db = fixture.CreateContext();
        Assert.Equal(5, await db.Users.CountAsync(x => ids.Contains(x.ID) && x.PhoneVerified, Ct));
    }

    // The limit is not a permission, and Users Write alone doesn't lift it.
    [Fact]
    public async Task A_high_limit_does_not_let_a_reader_change_users()
    {
        using var host = await HostAsync(authority: false, limit: 100, users: "\"r\"");
        var ids = await UsersAsync(1);
        var before = await StatesAsync(ids);

        foreach (var route in new[] { AssignRandomPasswords, ResetTotp, VerifyEmails, VerifyPhones })
        {
            using var response = await PostAsync(host, route, Selected(ids));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Equal(before, await StatesAsync(ids));
    }

    [Fact]
    public async Task With_the_authority_reset_totp_keeps_its_own_refusal()
    {
        using var host = await HostAsync(authority: true, limit: 2);

        using var response = await PostAsync(host, ResetTotp, Selected(await UsersAsync(3)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("identity authority", await response.Content.ReadAsStringAsync(Ct));
    }

    private static object Selected(IEnumerable<long> ids)
        => new SelectStateDTO<UserListDTO> { Items = ids.Select(x => new UserListDTO { ID = x.ToString() }).ToList() };

    private static Task<HttpResponseMessage> PostAsync(LegacyIdentityHttpHost<IdentityTestDbContext> host, string route, object selection)
        => host.Client.PostAsJsonAsync($"/api/IdentityUser/{route}", selection, Ct);

    // The envelope the user list's handlers read: they show the message.
    private static async Task AssertRefusedAsync(HttpResponseMessage response, int users, int limit)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{response.StatusCode}: {body}");

        var message = Prop(JsonDocument.Parse(body).RootElement, "Message");
        Assert.Equal("Bulk Write Limit Exceeded", Prop(message, "Title").GetString());
        Assert.Contains($"({users})", Prop(message, "Body").GetString());
        Assert.Contains($"maximum of {limit} ", Prop(message, "Body").GetString());
    }

    private static JsonElement Prop(JsonElement element, string name) =>
        element.EnumerateObject().First(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    // A host the administrator is signed in to. The authority's host is presented a token minted for its issuer.
    private async Task<LegacyIdentityHttpHost<IdentityTestDbContext>> HostAsync(bool authority, int? limit, string users = "\"r\",\"w\",\"d\"", bool registerTree = true)
    {
        var grant = limit is null ? "" : $$""","GeneralActionTree":{"DataGridBulkWriteMaxTop":{{limit}}}""";
        var tree = $$"""{"ShiftIdentityActions":{"Users":[{{users}}],"AccessTrees":["r"],"DataLevelAccess":["r","w","d","m"]}{{grant}} }""";
        var admin = $"{run}-admin";
        await fixture.CreateSyntheticUserAsync(admin, tree);

        var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, services =>
        {
            if (registerTree)
                services.Configure<TypeAuthAspNetCoreOptions>(o => o.AddActionTree<GeneralActionTree>());
        }, authority);

        using var minter = authority ? new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture) : null;
        if (minter is not null)
            minter.Services.GetRequiredService<ShiftIdentityConfiguration>().Token.Issuer = fixture.Options.Issuer;

        var token = (await (minter ?? host).DirectSignInAsync(admin, fixture.Password))!.Token;
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return host;
    }

    // Users every bulk action has something to do for: an unverified email and phone, and a TOTP secret.
    private async Task<List<long>> UsersAsync(int count)
    {
        var ids = new List<long>();
        for (var i = 0; i < count; i++)
            ids.Add(await fixture.CreateSyntheticUserAsync($"{run}-user-{i}", email: $"{run}-{i}@example.invalid"));

        await using var db = fixture.CreateContext();
        foreach (var user in await db.Users.Where(x => ids.Contains(x.ID)).ToListAsync(Ct))
        {
            user.Phone = $"+9647700{user.ID:D6}";
            user.TotpSecret = [1, 2, 3];
        }
        await db.SaveChangesAsync(Ct);
        return ids;
    }

    private sealed record State(string PasswordHash, bool HasTotp, bool PhoneVerified, string? VerificationToken, int Operations);

    private async Task<Dictionary<long, State>> StatesAsync(List<long> ids)
    {
        await using var db = fixture.CreateContext();
        var operations = await db.Set<AuthenticationOperation>().Where(x => ids.Contains(x.UserID)).Select(x => x.UserID).ToListAsync(Ct);
        var users = await db.Users.AsNoTracking().Where(x => ids.Contains(x.ID)).ToListAsync(Ct);

        return users.ToDictionary(x => x.ID, x => new State(Convert.ToBase64String(x.PasswordHash), x.TotpSecret is not null, x.PhoneVerified,
            x.VerificationSASToken, operations.Count(id => id == x.ID)));
    }
}
