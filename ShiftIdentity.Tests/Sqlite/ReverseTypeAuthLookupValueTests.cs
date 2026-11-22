using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.TypeAuth.AspNetCore;
using ShiftSoftware.TypeAuth.Core;
using ShiftSoftware.TypeAuth.Core.Actions;
using Xunit;

namespace ShiftIdentity.Tests.Sqlite;

/// <summary>
/// The reverse TypeAuth lookup on text and number actions: searched by a value, matched by the action's combine rule
/// (a number at least as great; a list that covers every entry), on a real identity host on SQLite in memory.
/// </summary>
[Trait("Category", "Http")]
public sealed class ReverseTypeAuthLookupValueTests(SqliteIdentityFixture fixture) : IClassFixture<SqliteIdentityFixture>, IAsyncLifetime
{
    private const string AdminName = "lookup-admin";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Names are unique per test so the class fixture's other users and trees never match.
    private readonly string run = Guid.NewGuid().ToString("N")[..8];

    public async ValueTask InitializeAsync()
    {
        fixture.Clock = TimeProvider.System;
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        if (!await db.Users.AnyAsync(x => x.Username == AdminName, Ct))
            await fixture.CreateSyntheticUserAsync(AdminName, FullAccessTree.BuildJson([typeof(ShiftIdentityActions)]));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task A_number_action_finds_whoever_holds_at_least_the_value()
    {
        var low = await UserAsync("low", Tree("""{"Discount":"50"}"""));
        var high = await UserAsync("high", null, await TreeAsync("discount-90", Tree("""{"Discount":"90"}""")));

        using var host = await HostAsync();

        var forty = await LookupAsync(host, Path(LookupTestActions.Discount), "40");
        Assert.Equal([low, high], Usernames(forty));
        Assert.Contains(Name("discount-90"), TreeNames(forty));

        var sixty = await LookupAsync(host, Path(LookupTestActions.Discount), "60");
        var only = Assert.Single(Users(sixty));
        Assert.Equal(high, Prop(only, "Username").GetString());
        Assert.False(Prop(only, "GrantedDirectly").GetBoolean());
        Assert.Equal([Name("discount-90")], Prop(only, "GrantingAccessTrees").EnumerateArray().Select(x => x.GetString()));

        Assert.Empty(Usernames(await LookupAsync(host, Path(LookupTestActions.Discount), "95")));
    }

    [Fact]
    public async Task A_list_action_finds_whoever_covers_every_entry_even_across_their_trees()
    {
        var user = await UserAsync("lists", Tree("""{"Warehouses":"A"}"""), await TreeAsync("warehouse-b", Tree("""{"Warehouses":"B"}""")));
        var all = await UserAsync("all", Tree("""{"Warehouses":"*"}"""));

        using var host = await HostAsync();

        var b = await LookupAsync(host, Path(LookupTestActions.Warehouses), "B");
        Assert.Equal([user, all], Usernames(b));
        Assert.Contains(Name("warehouse-b"), TreeNames(b));

        // Neither of the user's trees covers A and B alone; together they do, and both are credited.
        var both = await LookupAsync(host, Path(LookupTestActions.Warehouses), "A,B");
        Assert.Equal([user, all], Usernames(both));
        var match = Users(both).Single(x => Prop(x, "Username").GetString() == user);
        Assert.True(Prop(match, "GrantedDirectly").GetBoolean());
        Assert.Equal([Name("warehouse-b")], Prop(match, "GrantingAccessTrees").EnumerateArray().Select(x => x.GetString()));
        Assert.DoesNotContain(Name("warehouse-b"), TreeNames(both));
    }

    [Fact]
    public async Task A_dynamic_number_action_is_searched_on_a_row()
    {
        var user = await UserAsync("row", Tree("""{"BranchDiscount":{"7":"30"}}"""));

        using var host = await HostAsync();

        Assert.Equal([user], Usernames(await LookupAsync(host, Path(LookupTestActions.BranchDiscount), "20", "&DynamicId=7")));
        Assert.Empty(Usernames(await LookupAsync(host, Path(LookupTestActions.BranchDiscount), "20", "&DynamicId=8")));

        var anyRow = await RawLookupAsync(host, $"Access=Maximum&ActionPath={Path(LookupTestActions.BranchDiscount)}&Value=20&AnyDynamicRow=true");
        Assert.Equal(HttpStatusCode.BadRequest, anyRow.Status);
        Assert.Contains("one row at a time", anyRow.Body);
    }

    [Theory]
    [InlineData("", "require a Value")]
    [InlineData("abc", "is not a number")]
    [InlineData("0", "not more than the action's minimum")]
    public async Task A_value_that_cannot_be_searched_is_refused(string value, string message)
    {
        using var host = await HostAsync();

        var (status, body) = await RawLookupAsync(host, $"Access=Maximum&ActionPath={Path(LookupTestActions.Discount)}&Value={Uri.EscapeDataString(value)}&AnyDynamicRow=false");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(message, body);
    }

    private string Name(string name) => $"{name}-{run}";

    private static string Tree(string actions) => $$"""{"LookupTestActions":{{actions}}}""";

    private static string Path(ActionBase action)
    {
        // An action's path is set when an action tree holding it is first built.
        new TypeAuthContextBuilder().AddActionTree(typeof(LookupTestActions)).AddAccessTree("{}").Build();
        return action.Path!;
    }

    private async Task<long> TreeAsync(string name, string tree)
    {
        await using var db = fixture.CreateContext();
        var row = new AccessTree { Name = Name(name), Tree = tree };
        db.AccessTrees.Add(row);
        await db.SaveChangesAsync(Ct);
        return row.ID;
    }

    private async Task<string> UserAsync(string name, string? accessTree, params long[] namedTrees)
    {
        var username = Name(name);
        var id = await fixture.CreateSyntheticUserAsync(username, accessTree);
        await using var db = fixture.CreateContext();
        foreach (var tree in namedTrees)
            db.Set<UserAccessTree>().Add(new UserAccessTree { UserID = id, AccessTreeID = tree });
        await db.SaveChangesAsync(Ct);
        return username;
    }

    // The identity host with the test action tree registered beside ShiftIdentity's, signed in as an administrator.
    private async Task<ConfiguredIdentityHttpHost<IdentityTestDbContext>> HostAsync()
    {
        var host = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture, configureServices: services =>
            services.Configure<TypeAuthAspNetCoreOptions>(o => o.AddActionTree<LookupTestActions>()));

        using var response = await host.Client.PostAsJsonAsync("api/identity/v2/login",
            new PasswordLoginRequest(AdminName, fixture.Password, IdentityHttpHost.Pkce().Challenge), Ct);
        var session = Prop(await response.Content.ReadFromJsonAsync<JsonElement>(Ct), "Session");
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Prop(session, "Token").GetString());
        return host;
    }

    private async Task<JsonElement> LookupAsync(ConfiguredIdentityHttpHost<IdentityTestDbContext> host, string path, string value, string extra = "")
    {
        var (status, body) = await RawLookupAsync(host, $"Access=Maximum&ActionPath={path}&Value={Uri.EscapeDataString(value)}&AnyDynamicRow=false{extra}");
        Assert.True(status == HttpStatusCode.OK, $"{status}: {body}");
        return Prop(JsonDocument.Parse(body).RootElement.Clone(), "Entity");
    }

    private static async Task<(HttpStatusCode Status, string Body)> RawLookupAsync(ConfiguredIdentityHttpHost<IdentityTestDbContext> host, string query)
    {
        using var response = await host.Client.GetAsync($"api/ReverseTypeAuthLookup?{query}", Ct);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    private IEnumerable<JsonElement> Users(JsonElement entity) =>
        Prop(entity, "Users").EnumerateArray().Where(x => Prop(x, "Username").GetString()!.EndsWith(run, StringComparison.Ordinal));

    private string[] Usernames(JsonElement entity) => Users(entity).Select(x => Prop(x, "Username").GetString()!).ToArray();

    private static string[] TreeNames(JsonElement entity) => Prop(entity, "AccessTrees").EnumerateArray().Select(x => Prop(x, "Name").GetString()!).ToArray();

    private static JsonElement Prop(JsonElement element, string name) =>
        element.EnumerateObject().First(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    [ActionTree("Lookup Test Actions", "")]
    public class LookupTestActions
    {
        public static readonly DecimalAction Discount = new("Discount", null, 0, 100);
        public static readonly TextAction Warehouses = new("Warehouses", TextCombine.UnionList(","), null, maximumAccess: "*");
        public static readonly DynamicDecimalAction BranchDiscount = new("Branch Discount", null, 0, 100);
    }
}
