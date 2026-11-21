using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using OtpNet;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Auth;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests.Sqlite;

/// <summary>
/// A real identity host (the public dashboard registration with the authority enabled) on a SQLite database in memory:
/// startup, the v2 login and its follow-ups, the dashboard's CRUD and lists, and parallel requests. Nothing here needs
/// SQL Server.
/// </summary>
[Trait("Category", "Http")]
public sealed class SqliteIdentityHostTests(SqliteIdentityFixture fixture) : IClassFixture<SqliteIdentityFixture>, IAsyncLifetime
{
    private const string AdminName = "sqlite-admin";
    private const string NewPassword = "A new synthetic phrase 83!";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        fixture.Clock = TimeProvider.System;
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        if (!await db.Users.AnyAsync(x => x.Username == AdminName, Ct))
            await fixture.CreateSyntheticUserAsync(AdminName, FullAccessTree.BuildJson([typeof(ShiftIdentityActions)]));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private ConfiguredIdentityHttpHost<IdentityTestDbContext> Host() => new(fixture);

    private static async Task<JsonElement> LoginAsync(HttpClient client, string username, string password)
    {
        using var response = await client.PostAsJsonAsync("api/identity/v2/login", new PasswordLoginRequest(username, password, IdentityHttpHost.Pkce().Challenge), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Prop(await response.Content.ReadFromJsonAsync<JsonElement>(Ct), "Session");
    }

    private static async Task SignInAsAdminAsync(ConfiguredIdentityHttpHost<IdentityTestDbContext> host, string password = "1") =>
        host.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Prop(await LoginAsync(host.Client, AdminName, password), "Token").GetString());

    private static JsonElement Prop(JsonElement element, string name) =>
        element.EnumerateObject().First(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode? expected = null)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(Ct);
            if (expected is { } status) Assert.True(response.StatusCode == status, $"{response.StatusCode}: {body}");
            else Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
            return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
        }
    }

    [Fact]
    public async Task The_host_starts_and_signs_in_refreshes_and_serves_the_profile()
    {
        using var host = Host();
        var session = await LoginAsync(host.Client, fixture.Username, fixture.Password);
        var access = Prop(session, "Token").GetString()!;
        Assert.Equal("2", new JsonWebToken(access).GetPayloadValue<string>("shift_schema"));

        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var profile = await ReadAsync(await host.Client.GetAsync("api/UserManager/UserData", Ct));
        Assert.Equal(fixture.Username, Prop(Prop(profile, "Entity"), "Username").GetString());
        await ReadAsync(await host.Client.PostAsJsonAsync("api/Auth/Refresh", new { RefreshToken = Prop(session, "RefreshToken").GetString() }, Ct));

        await using var db = fixture.CreateContext();
        Assert.Contains("SessionIssued", await db.Set<AuthenticationAuditEvent>().Select(x => x.Outcome).ToListAsync(Ct));
    }

    [Fact]
    public async Task Password_change_then_MFA_enrolment_then_an_MFA_sign_in()
    {
        using var host = new IdentityHttpHost(fixture);
        var pkce = IdentityHttpHost.Pkce();
        var session = Assert.IsType<SessionIssued>(await host.LoginAsync(fixture, pkce.Challenge)).Session;

        // Password change.
        var start = Assert.IsType<ChallengeRequired>(await host.StartPasswordChangeAsync(session.Token, pkce.Challenge)).Challenge;
        var proven = Assert.IsType<ChallengeRequired>(await host.ProvePasswordAsync(start.Handle!, fixture.Password, pkce.Verifier)).Challenge;
        Assert.Equal(AuthenticationStep.PasswordChange, proven.Step);
        var changed = Assert.IsType<PasswordChanged>(await host.ChangePasswordAsync(proven.Handle!, NewPassword, pkce.Verifier));
        session = Assert.IsType<SessionIssued>(changed.Continuation).Session;
        Assert.IsType<AuthenticationRefused>(await host.LoginAsync(fixture, pkce.Challenge));

        // MFA enrolment.
        var mfa = Assert.IsType<ChallengeRequired>(await host.StartMfaAsync(session.Token, pkce.Challenge)).Challenge;
        var setup = Assert.IsType<ChallengeRequired>(await host.MfaPasswordAsync(mfa.Handle!, NewPassword, pkce.Verifier)).Challenge;
        Assert.Equal(AuthenticationStep.NewMfa, setup.Step);
        var secret = Base32Encoding.ToBytes(setup.NewAuthenticator!.Secret);
        var enrolled = Assert.IsType<MfaChanged>(await host.ConfirmFactorAsync(setup.Handle!,
            new Totp(secret).ComputeTotp(fixture.Clock.GetUtcNow().UtcDateTime), pkce.Verifier));
        Assert.IsType<SessionIssued>(enrolled.Continuation);

        // The next sign-in asks for the factor.
        var login = Assert.IsType<ChallengeRequired>(await host.LoginAsync(fixture, pkce.Challenge, NewPassword)).Challenge;
        Assert.Equal(AuthenticationStep.ExistingMfa, login.Step);
    }

    [Fact]
    public async Task An_auth_code_for_another_client_is_exchanged_for_its_session()
    {
        using var host = Host();
        var access = Prop(await LoginAsync(host.Client, fixture.Username, fixture.Password), "Token").GetString()!;
        var verifier = Guid.NewGuid().ToString();

        using var create = new HttpRequestMessage(HttpMethod.Post, "api/Auth/AuthCode")
        {
            Content = JsonContent.Create(new GenerateAuthCodeDTO { AppId = "other-client", ReturnUrl = "/home", CodeChallenge = HashService.SHA512GenerateHash(verifier) }),
        };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var code = Prop(Prop(await ReadAsync(await host.Client.SendAsync(create, Ct)), "Entity"), "Code").GetGuid();

        var exchanged = await ReadAsync(await host.Client.PostAsJsonAsync("api/Auth/TokenWithAppIdOnly",
            new GenerateExternalTokenWithAppIdOnlyDTO { AppId = "other-client", AuthCode = code, CodeVerifier = verifier }, Ct));
        var token = Prop(Prop(exchanged, "Entity"), "Token").GetString()!;
        Assert.Equal("other-client", new JsonWebToken(token).GetPayloadValue<string>("shift_client"));
    }

    [Fact]
    public async Task An_administrator_creates_a_user_who_then_signs_in()
    {
        using var host = Host();
        await SignInAsAdminAsync(host);
        await using var db = fixture.CreateContext();
        var branch = (await db.Users.AsNoTracking().SingleAsync(x => x.ID == fixture.UserID, Ct)).CompanyBranchID!.Value.ToString();
        var user = new UserDTO
        {
            Username = "sqlite-created-" + Guid.NewGuid().ToString("N")[..8], FullName = "Created On SQLite", IsActive = true, AccessTree = "{}",
            CompanyBranchID = new ShiftEntitySelectDTO { Value = branch }, Password = NewPassword, RequireChangeAtNextLogin = false, SendVerification = false,
        };

        await ReadAsync(await host.Client.PostAsJsonAsync("api/IdentityUser", user, Ct), HttpStatusCode.Created);

        Assert.Equal(JsonValueKind.Object, (await LoginAsync(host.Client, user.Username, NewPassword)).ValueKind);
    }

    [Theory]
    [InlineData("api/IdentityCountry", "{\"Name\":\"Iraq\",\"CallingCode\":\"+964\"}")]
    [InlineData("api/IdentityBrand", "{\"Name\":\"Toyota\"}")]
    [InlineData("api/IdentityDepartment", "{\"Name\":\"Sales\"}")]
    [InlineData("api/IdentityService", "{\"Name\":\"Repair\"}")]
    public async Task The_dashboard_creates_reads_updates_lists_and_deletes(string route, string json)
    {
        using var host = Host();
        await SignInAsAdminAsync(host);
        StringContent Body(string text) => new(text, System.Text.Encoding.UTF8, "application/json");

        var created = Prop(await ReadAsync(await host.Client.PostAsync(route, Body(json), Ct)), "Entity");
        var id = Prop(created, "ID").GetString()!;
        var updated = JsonSerializer.Deserialize<Dictionary<string, object?>>(created.GetRawText())!;
        updated[updated.Keys.First(k => k.Equals("Name", StringComparison.OrdinalIgnoreCase))] = "Renamed";
        await ReadAsync(await host.Client.PutAsync($"{route}/{id}", Body(JsonSerializer.Serialize(updated)), Ct));

        Assert.Equal("Renamed", Prop(Prop(await ReadAsync(await host.Client.GetAsync($"{route}/{id}", Ct)), "Entity"), "Name").GetString());
        var list = Prop(await ReadAsync(await host.Client.GetAsync($"{route}?$top=5&$orderby=ID desc", Ct)), "Value");
        Assert.Equal(id, Prop(list[0], "ID").GetString());

        await ReadAsync(await host.Client.DeleteAsync($"{route}/{id}", Ct));
        var after = Prop(await ReadAsync(await host.Client.GetAsync($"{route}?$top=5&$orderby=ID desc", Ct)), "Value");
        Assert.DoesNotContain(after.EnumerateArray(), x => Prop(x, "ID").GetString() == id);
    }

    [Theory]
    [InlineData("api/IdentityUser?$top=5&$orderby=LastSeen desc")]
    [InlineData("api/IdentityUser?$top=5&$filter=LastSeen lt 2100-01-01T00:00:00Z")]
    [InlineData("api/IdentityAccessTree?$top=5")]
    [InlineData("api/IdentityApp?$top=5")]
    [InlineData("api/IdentityCity?$top=5")]
    [InlineData("api/IdentityCompany?$top=5")]
    [InlineData("api/IdentityCompanyBranch?$top=5")]
    [InlineData("api/IdentityCompanyCalendar?$top=5")]
    [InlineData("api/IdentityRegion?$top=5")]
    [InlineData("api/IdentityTeam?$top=5")]
    public async Task Every_dashboard_list_is_served(string route)
    {
        using var host = Host();
        await SignInAsAdminAsync(host);

        Assert.Equal(JsonValueKind.Array, Prop(await ReadAsync(await host.Client.GetAsync(route, Ct)), "Value").ValueKind);
    }

    [Fact]
    public Task The_company_list_names_each_brand_once() => CompanyListChecks.EachBrandIsListedOnceAsync(fixture, Ct);

    [Fact]
    public async Task Parallel_sign_ins_of_different_users_and_dashboard_reads_all_succeed()
    {
        var users = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            var name = $"sqlite-parallel-{i}";
            await using var db = fixture.CreateContext();
            if (!await db.Users.AnyAsync(x => x.Username == name, Ct)) await fixture.CreateSyntheticUserAsync(name);
            users.Add(name);
        }
        using var host = Host();
        await SignInAsAdminAsync(host);
        var token = host.Client.DefaultRequestHeaders.Authorization;

        async Task<(string, HttpStatusCode)> Read(string route)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.Authorization = token;
            using var response = await host.Client.SendAsync(request, Ct);
            return (route, response.StatusCode);
        }

        var requests = users.SelectMany(user => new[]
        {
            SignInAsync(host.Client, user), Read("api/IdentityUser?$top=5"), Read("api/IdentityCompanyBranch?$top=5"), Read("api/UserManager/UserData"),
        }).ToArray();
        var results = await Task.WhenAll(requests);

        Assert.All(results, r => Assert.True(r.Item2 == HttpStatusCode.OK, $"{r.Item1}: {r.Item2}"));
    }

    [Fact]
    public async Task Simultaneous_sign_ins_of_one_user_each_succeed_or_are_refused_as_a_conflict()
    {
        // SQL Server makes them wait for each other; on any other database the row version refuses the ones that lose
        // the race (PLAN D19). None of them fails any other way, and at least one is signed in.
        using var host = Host();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => SignInAsync(host.Client, fixture.Username)));

        Assert.All(results, r => Assert.Contains(r.Item2, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        Assert.Contains(results, r => r.Item2 == HttpStatusCode.OK);
    }

    private async Task<(string, HttpStatusCode)> SignInAsync(HttpClient client, string username)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/identity/v2/login")
            { Content = JsonContent.Create(new PasswordLoginRequest(username, fixture.Password, IdentityHttpHost.Pkce().Challenge)) };
        using var response = await client.SendAsync(request, Ct);
        return ("sign-in " + username, response.StatusCode);
    }
}

/// <summary>A SQLite host on a database file, and a SQLite host whose schema was never created.</summary>
[Trait("Category", "Http")]
public sealed class SqliteIdentityStorageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task On_a_file_the_data_is_still_there_after_the_host_restarts()
    {
        await using var fixture = new SqlIdentityFixture { Database = IdentityTestDatabase.SqliteFile };
        await fixture.InitializeAsync();

        using (var first = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture))
            await IdentityHttpHost.SignInAsync(first.Client, fixture.Username, fixture.Password);
        SqliteConnection.ClearAllPools();   // nothing is held in memory between the two hosts

        using var second = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture);
        await using (var db = fixture.CreateContext())
        {
            Assert.Contains("SessionIssued", await db.Set<AuthenticationAuditEvent>().Select(x => x.Outcome).ToListAsync(Ct));
            Assert.Equal(1, await db.Apps.CountAsync(x => x.AppId == ConfiguredIdentityHttpHost<IdentityTestDbContext>.ClientId, Ct));
        }
        await IdentityHttpHost.SignInAsync(second.Client, fixture.Username, fixture.Password);
    }

    [Fact]
    public async Task A_host_whose_schema_was_not_created_stops_with_a_message_that_says_to_create_it()
    {
        await using var fixture = new SqliteIdentityFixture();
        await fixture.InitializeAsync();
        var empty = $"Data Source=empty-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        fixture.ContextFactory = _ => new IdentityTestDbContext(new DbContextOptionsBuilder().UseSqlite(empty).Options);

        var refused = Assert.ThrowsAny<Exception>(() => new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture));

        var message = (refused as InvalidOperationException ?? refused.InnerException)?.Message ?? refused.Message;
        Assert.Contains("EnsureCreated", message);
        Assert.Contains("UserSecurityStates", message);
    }
}
