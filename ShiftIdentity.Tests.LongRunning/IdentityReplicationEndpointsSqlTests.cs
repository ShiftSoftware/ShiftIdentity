using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Endpoints;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Replication;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Replication;
using ShiftSoftware.TypeAuth.AspNetCore.Extensions;
using ShiftSoftware.TypeAuth.Core;
using Xunit;
using static ShiftSoftware.ShiftEntity.Model.Replication.IdentityDatabaseAndContainerNames;

namespace ShiftIdentity.Tests;

/// <summary>
/// The dashboard's <c>api/IdentityReplication</c> endpoints: the Identity catch-up, run from the API with the caller's
/// own token. Whoever may write an entity may rebuild its documents; <c>all</c> rebuilds every entity the caller may
/// write and lists the rest as skipped.
/// </summary>
public class IdentityReplicationEndpointsSqlTests
{
    private const string Database = "identity";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Access trees as an admin would hold them.
    private const string BrandsWriter = "{\"ShiftIdentityActions\":{\"Brands\":[\"r\",\"w\"]}}";
    private const string BrandsReader = "{\"ShiftIdentityActions\":{\"Brands\":[\"r\"]}}";
    private const string BranchesWriter = "{\"ShiftIdentityActions\":{\"CompanyBranches\":[\"r\",\"w\"]}}";
    private const string UsersWriter = "{\"ShiftIdentityActions\":{\"Users\":[\"r\",\"w\"]}}";

    [Fact]
    public async Task Whoever_may_write_brands_can_rebuild_the_brands()
    {
        await using var host = await Host.StartAsync("ReplicationBrands");
        await host.AddAsync(new Brand { Name = "Toyota" }, new Brand { Name = "Lexus" });

        var (status, body) = await host.PostAsync("brands", BrandsWriter);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body!.Succeeded, body.Report);
        Assert.Equal("Brand: 2 of 2 rows replicated.", body.Report);
        Assert.Equal(2, host.Cosmos.Stored(BrandContainerName).Count);
    }

    [Fact]
    public async Task Writing_brands_does_not_let_you_rebuild_the_branches()
    {
        await using var host = await Host.StartAsync("ReplicationBranchesRefused");
        await host.AddAsync(Branch());

        var (status, _) = await host.PostAsync("branches", BrandsWriter);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Empty(host.Cosmos.Writes);
    }

    [Fact]
    public async Task Reading_brands_is_not_enough_to_rebuild_them()
    {
        await using var host = await Host.StartAsync("ReplicationReadOnly");
        await host.AddAsync(new Brand { Name = "Toyota" });

        Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync("brands", BrandsReader)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync("all", BrandsReader)).Status);
        Assert.Empty(host.Cosmos.Writes);
    }

    [Fact]
    public async Task Without_a_token_nothing_runs()
    {
        await using var host = await Host.StartAsync("ReplicationAnonymous");
        await host.AddAsync(new Brand { Name = "Toyota" });

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.PostAsync("brands", accessTree: null)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.PostAsync("all", accessTree: null)).Status);
        Assert.Empty(host.Cosmos.Writes);
    }

    // The deployment this was made for: fill Branches from the API before the Functions app that reads it goes live.
    [Fact]
    public async Task Rebuilding_the_branches_fills_Branches_with_one_document_per_branch_and_leaves_CompanyBranches_alone()
    {
        await using var host = await Host.StartAsync("ReplicationBranches");
        await host.AddAsync(Branch("Erbil"), Branch("Duhok"));

        var (status, body) = await host.PostAsync("branches", BranchesWriter);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("CompanyBranch: 2 of 2 rows replicated.", body!.Report);
        Assert.Equal(["Duhok", "Erbil"], host.Cosmos.Stored(BranchContainerName).Select(x => (string)x["City"]!["Name"]!).Order());
        Assert.DoesNotContain(host.Cosmos.Writes, x => x.Container == CompanyBranchContainerName);
    }

    [Fact]
    public async Task All_rebuilds_what_you_may_write_in_the_usual_order_and_lists_the_rest_as_skipped()
    {
        await using var host = await Host.StartAsync("ReplicationAll");
        await host.AddAsync(Branch(), new Brand { Name = "Toyota" });

        // Branches before brands in the tree's order, and brands before branches in the catch-up's.
        var (status, body) = await host.PostAsync("all", "[" + BranchesWriter + "," + BrandsWriter + "]");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["Brand", "CompanyBranch"], body!.Steps.Select(x => x.Entity));
        Assert.Equal(
            ["Service", "Department", "Region", "Country", "City", "Company", "Team", "User", "AccessTree", "UserAccessTree", "TeamUser"],
            body.Skipped);
        Assert.Single(host.Cosmos.Stored(BrandContainerName));
        Assert.Single(host.Cosmos.Stored(BranchContainerName));
        Assert.Empty(host.Cosmos.Stored(CompanyContainerName));
    }

    // Which users hold a tree is edited on the user, so whoever may write users may rebuild those too.
    [Fact]
    public async Task Whoever_may_write_users_can_rebuild_the_users_and_their_access_trees()
    {
        await using var host = await Host.StartAsync("ReplicationUsers");

        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync("users", UsersWriter)).Status);
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync("user-access-trees", UsersWriter)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync("access-trees", UsersWriter)).Status);
    }

    // TCA Identity replicates the organization only.
    [Fact]
    public async Task A_host_that_does_not_replicate_users_has_no_user_endpoints_and_all_leaves_them_out()
    {
        await using var host = await Host.StartAsync("ReplicationOrganization", entities: IdentityReplicationEntities.Organization);

        Assert.Equal(HttpStatusCode.NotFound, (await host.PostAsync("users", UsersWriter)).Status);

        var (status, body) = await host.PostAsync("all", "[" + UsersWriter + "," + BrandsWriter + "]");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["Brand"], body!.Steps.Select(x => x.Entity));
        Assert.DoesNotContain("User", body.Skipped);
    }

    [Fact]
    public async Task A_row_that_cannot_be_written_answers_400_with_the_report_and_stays_dirty()
    {
        // No Branches container: the state before it is created.
        await using var host = await Host.StartAsync("ReplicationMissingBranches", withBranches: false);
        var branch = Branch();
        await host.AddAsync(branch);

        var (status, body) = await host.PostAsync("branches", BranchesWriter);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(body!.Succeeded);
        Assert.StartsWith("CompanyBranch: 1 of 1 rows failed and stay dirty for the next run.", body.Report);
        await host.WithDbAsync(async db =>
            Assert.Null(await db.CompanyBranches.Where(x => x.ID == branch.ID).Select(x => x.LastReplicationDate).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Only_dirty_rebuilds_only_the_rows_not_yet_replicated()
    {
        await using var host = await Host.StartAsync("ReplicationOnlyDirty");
        await host.AddAsync(new Brand { Name = "Toyota" });

        Assert.Equal("Brand: 1 of 1 rows replicated.", (await host.PostAsync("brands", BrandsWriter)).Body!.Report);
        Assert.Equal("Brand: 0 of 0 rows replicated.", (await host.PostAsync("brands?onlyDirty=true", BrandsWriter)).Body!.Report);
        Assert.Equal("Brand: 1 of 1 rows replicated.", (await host.PostAsync("brands", BrandsWriter)).Body!.Report);
    }

    // A branch with its city, region, country and company.
    private static CompanyBranch Branch(string city = "Erbil")
    {
        var region = new Region { Name = "KRG", Country = new Country { Name = "Iraq", CallingCode = "+964" } };
        return new CompanyBranch { Name = city, City = new City { Name = city, Region = region }, Region = region, Company = new Company { Name = "Shift" } };
    }

    // An API with only the replication endpoints, on its own SQL catalog. The caller's access trees come in a header,
    // as a JSON array or a single tree, where a real host reads them from the token.
    private sealed class Host : IAsyncDisposable
    {
        private OwnedSqlCatalog catalog = null!;
        private TestServer server = null!;
        private HttpClient client = null!;

        public RecordingCosmos Cosmos { get; private init; } = null!;

        public static async Task<Host> StartAsync(string name, bool withBranches = true, IEnumerable<IdentityReplicationEntity>? entities = null)
        {
            var cosmos = new RecordingCosmos(Database)
                .Container(CompanyBranchContainerName, "/BranchID", "/ItemType")
                .Container(CountryContainerName, "/CountryID", "/RegionID", "/ItemType")
                .Container(CompanyContainerName, "/id")
                .Container(ServiceContainerName, "/id")
                .Container(DepartmentContainerName, "/id")
                .Container(BrandContainerName, "/id")
                .Container(TeamContainerName, "/id")
                .Container(UserContainerName, "/id")
                .Container(AccessTreeContainerName, "/id")
                .Container(UserAccessTreeContainerName, "/id")
                .Container(TeamUserContainerName, "/id");
            if (withBranches)
                cosmos.Container(BranchContainerName, "/id");

            var host = new Host { Cosmos = cosmos };
            host.catalog = await OwnedSqlCatalog.CreateAsync(name, Ct);

            host.server = new TestServer(new WebHostBuilder().UseEnvironment("Testing").ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddLogging();
                services.AddHttpContextAccessor();
                services.AddAuthentication(AccessTreeHeaderHandler.Scheme)
                    .AddScheme<AuthenticationSchemeOptions, AccessTreeHeaderHandler>(AccessTreeHeaderHandler.Scheme, null);
                services.AddAuthorization();
                services.AddTypeAuth(o => o.AddActionTree<ShiftIdentityActions>());
                services.AddDbContext<IdentityTestDbContext>(o => o.UseSqlServer(host.catalog.ConnectionString));
                services.AddShiftEntityCosmosDbReplication<IdentityTestDbContext>();
                services.AddShiftIdentityReplicationMapper();
                services.AddShiftIdentityReplicationEndpoints<IdentityTestDbContext>(_ => cosmos.Client, Database, entities);
            }).Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapIdentityReplicationEndpoints());
            }));
            host.client = host.server.CreateClient();

            await host.WithDbAsync(db => db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(Ct));
            return host;
        }

        public Task AddAsync(params object[] rows) => WithDbAsync(async db =>
        {
            db.AddRange(rows);
            await db.SaveChangesAsync(Ct);
        });

        public async Task WithDbAsync(Func<IdentityTestDbContext, Task> work)
        {
            await using var scope = server.Services.CreateAsyncScope();
            await work(scope.ServiceProvider.GetRequiredService<IdentityTestDbContext>());
        }

        public async Task<(HttpStatusCode Status, IdentityReplicationResponse? Body)> PostAsync(string route, string? accessTree)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/IdentityReplication/" + route);
            if (accessTree is not null)
                request.Headers.Add(AccessTreeHeaderHandler.Header, accessTree);

            using var response = await client.SendAsync(request, Ct);
            var body = response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest
                ? await response.Content.ReadFromJsonAsync<IdentityReplicationResponse>(Ct)
                : null;
            return (response.StatusCode, body);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            server.Dispose();
            await catalog.DisposeAsync();
            Cosmos.Dispose();
        }
    }

    // Signs in whoever sends access trees in the header, with those trees as their claims.
    private sealed class AccessTreeHeaderHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Scheme = "AccessTreeHeader";
        public const string Header = "X-Access-Trees";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var value = Request.Headers[Header].ToString();
            if (value.Length == 0)
                return Task.FromResult(AuthenticateResult.NoResult());

            var trees = value.StartsWith('[')
                ? System.Text.Json.JsonDocument.Parse(value).RootElement.EnumerateArray().Select(x => x.GetRawText())
                : [value];
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "admin"), .. trees.Select(x => new Claim(TypeAuthClaimTypes.AccessTree, x))], Scheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
        }
    }
}
