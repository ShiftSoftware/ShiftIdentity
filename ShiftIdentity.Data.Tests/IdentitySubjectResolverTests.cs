using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftIdentity.Data.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.Flags;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Team;
using ShiftSoftware.ShiftIdentity.Data.Authorization;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.IdentityReference;
using ShiftSoftware.ShiftIdentity.Data.IdentityReference.Cosmos;
using ShiftSoftware.ShiftIdentity.Data.Replication;
using ShiftSoftware.TypeAuth.Core;
using ShiftSoftware.TypeAuth.Core.Actions;
using Xunit;
using Claims = ShiftSoftware.ShiftEntity.Core.Constants;

namespace ShiftIdentity.Data.Tests;

/// <summary>
/// The identity subject resolver: one outcome per candidate user, prepared from the replicated documents with the claims
/// and grants of sign-in, reused within a scope. The record check that follows is the repository's CanAccessAs, and the
/// end-to-end tests below give the answers the previous lookup gave on the same fixtures.
/// </summary>
public class IdentitySubjectResolverTests
{
    private const string SystemGrant = """{"ShiftIdentityActions":{"Users":["Read"]}}""";
    private static readonly string ScopeGrant = """
        {"ShiftIdentityActions":{"DataLevelAccess":{
            "Companies":{"@self@":["Read"]},"Teams":{"@self@":["Read"]}
        }}}
        """.Replace("@self@", TypeAuthContext.SelfReferenceKey);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Both users have their own system grant and the named tree 20, which grants their own company and teams.
    private static UserModel ActiveUser(long id, long company = 4) => new()
    {
        id = id.ToString(), IsActive = true, Username = "user-" + id, FullName = "User " + id,
        CompanyID = company, CompanyBranchID = 8, RegionID = 9, CountryID = 10,
        AccessTree = SystemGrant, AccessTreeIDs = [20],
    };

    private static TeamModel Team(long id, params long[] members) => new() { id = id.ToString(), Name = "Team " + id, UserIDs = [.. members] };

    private static StubIdentityReferenceSource Source(params UserModel[] users)
    {
        var source = new StubIdentityReferenceSource();
        source.Seed(users.Length > 0 ? users : [ActiveUser(1), ActiveUser(2, company: 5)]);
        source.Seed(new CompanyModel { id = "4" }, new CompanyModel { id = "5" });
        source.Seed(new CompanyBranchModel { id = "8", CityID = 7 });
        source.Seed(new AccessTreeModel { id = "20", Name = "Scope", Tree = ScopeGrant });
        source.Seed(Team(11, 1), Team(12, 1, 2));
        return source;
    }

    // What a host registers: its action trees, through its TypeAuth registration.
    private static ITypeAuthService HostTypeAuth() => new TypeAuthContextBuilder().AddActionTree<ShiftIdentityActions>().Build();

    private static IdentitySubjectResolver Resolver(IIdentityReferenceSource source) => new(source, HostTypeAuth(), new EncodedIds());

    private static Task<IdentitySubjectResolution> ResolveAsync(IIdentityReferenceSource source, params long[] userIds)
        => Resolver(source).ResolveAsync(userIds, Ct);

    // ---- Claims and grants ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Claims_and_grants_are_those_of_sign_in()
    {
        var resolution = await ResolveAsync(Source(), 1, 2);

        Assert.Equal(new long[] { 1, 2 }, resolution.Eligible.Select(subject => subject.UserId));
        Assert.Empty(resolution.NotEligible);
        Assert.Empty(resolution.Unresolved);

        var user = resolution.Eligible[0].GetUser();
        Assert.True(user.Identity!.IsAuthenticated);
        Assert.Equal("UserDTO:1", user.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("user-1", user.FindFirst(ClaimTypes.Name)?.Value);
        Assert.Equal("CompanyDTO:4", user.FindFirst(Claims.CompanyIdClaim)?.Value);
        Assert.Equal("CompanyBranchDTO:8", user.FindFirst(Claims.CompanyBranchIdClaim)?.Value);
        Assert.Equal("RegionDTO:9", user.FindFirst(Claims.RegionIdClaim)?.Value);
        Assert.Equal("CityDTO:7", user.FindFirst(Claims.CityIdClaim)?.Value);
        Assert.Equal("CountryDTO:10", user.FindFirst(Claims.CountryIdClaim)?.Value);
        Assert.Equal(new[] { "TeamDTO:11", "TeamDTO:12" }, user.FindAll(Claims.TeamIdsClaim).Select(claim => claim.Value));

        // The direct tree and the named tree are merged.
        Assert.True(resolution.Eligible[0].TypeAuth.Can(ShiftIdentityActions.Users, Access.Read));
        Assert.True(resolution.Eligible[0].TypeAuth.Can(ShiftIdentityActions.DataLevelAccess.Companies, Access.Read,
            "CompanyDTO:4", user.FindFirst(Claims.CompanyIdClaim)!.Value));
    }

    // Sign-in adds the named trees in the order of the assignments, and the user document lists them in that order. The
    // merge depends on it: a text action without a comparer takes the value of the last tree that sets it.
    [Fact]
    public async Task Named_trees_merge_in_the_order_of_the_document_as_at_sign_in()
    {
        var typeAuth = new TypeAuthContextBuilder().AddActionTree<ShiftIdentityActions>().AddActionTree<OrderedActions>().Build();
        async Task<string?> GreetingAsync(params long[] assigned)
        {
            var user = ActiveUser(1);
            user.AccessTreeIDs = [.. assigned];
            var source = Source(user);
            source.Seed(new AccessTreeModel { id = "21", Name = "First", Tree = """{"OrderedActions":{"Greeting":"first"}}""" },
                new AccessTreeModel { id = "22", Name = "Second", Tree = """{"OrderedActions":{"Greeting":"second"}}""" });
            var subject = (await new IdentitySubjectResolver(source, typeAuth, new EncodedIds()).ResolveAsync([1], Ct)).Eligible.Single();
            return subject.TypeAuth.AccessValue(OrderedActions.Greeting);
        }

        Assert.Equal("second", await GreetingAsync(21, 22));
        Assert.Equal("first", await GreetingAsync(22, 21));
    }

    [Fact]
    public async Task GetUser_returns_a_copy()
    {
        var subject = (await ResolveAsync(Source(), 1)).Eligible.Single();

        ((ClaimsIdentity)subject.GetUser().Identity!).AddClaim(new(Claims.CompanyIdClaim, "CompanyDTO:5"));

        Assert.Single(subject.GetUser().FindAll(Claims.CompanyIdClaim));
    }

    [Fact]
    public async Task An_empty_own_tree_means_no_direct_grants_as_at_sign_in()
    {
        var user = ActiveUser(1);
        user.AccessTree = null;

        var subject = (await ResolveAsync(Source(user), 1)).Eligible.Single();

        Assert.False(subject.TypeAuth.Can(ShiftIdentityActions.Users, Access.Read));
    }

    [Fact]
    public async Task Soft_deleted_named_trees_and_teams_still_apply_as_at_sign_in()
    {
        var source = Source();
        source.Seed(new AccessTreeModel { id = "20", Name = "Still assigned", Tree = ScopeGrant, IsDeleted = true });
        var deletedTeam = Team(11, 1);
        deletedTeam.IsDeleted = true;
        source.Seed(deletedTeam, Team(12, 1, 2));

        var subject = (await ResolveAsync(source, 1)).Eligible.Single();

        Assert.Equal(new[] { "TeamDTO:11", "TeamDTO:12" }, subject.GetUser().FindAll(Claims.TeamIdsClaim).Select(claim => claim.Value));
        Assert.True(subject.TypeAuth.Can(ShiftIdentityActions.DataLevelAccess.Companies, Access.Read, "CompanyDTO:4", "CompanyDTO:4"));
    }

    // ---- The record check, end to end -----------------------------------------------------------------------------

    public sealed class Record : ShiftEntity<Record>
    {
        public long? CompanyID { get; set; }
        public long? TeamID { get; set; }
    }

    public sealed class RecordDTO : ShiftEntityDTOBase
    {
        public override string? ID { get; set; }
    }

    public sealed class RecordsDb(DbContextOptions<RecordsDb> options) : ShiftDbContext(options)
    {
        public DbSet<Record> Records => Set<Record>();
    }

    // The repository the old lookup's policy described: a company dimension and a team dimension, each on the user's own
    // claims. It is built for a request user with no claims at all, so an answer cannot come from the request.
    private static async Task<IReadOnlyList<long>> WhoCanAsync(IReadOnlyList<IdentityAccessSubject> subjects, Access access,
        Func<IdentityAccessSubject, Access, bool> screenPermission)
    {
        var services = new ServiceCollection();
        services.AddDbContext<RecordsDb>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddSingleton<IHashIdService, EncodedIds>();
        services.AddScoped<ICurrentUserProvider, NoUser>();
        services.AddScoped<IdentityClaimProvider>();
        services.AddScoped<IDefaultDataLevelAccess, NoLegacyRules>();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();

        var repository = new ShiftRepository<RecordsDb, Record, RecordDTO, RecordDTO>(
            scope.ServiceProvider.GetRequiredService<RecordsDb>(), options => options.DataLevelAccess(declare =>
            {
                declare.On(ShiftIdentityActions.DataLevelAccess.Companies).Key(record => record.CompanyID)
                    .HashId<CompanyDTO>().Self(Claims.CompanyIdClaim);
                declare.On(ShiftIdentityActions.DataLevelAccess.Teams).Key(record => record.TeamID)
                    .HashId<TeamDTO>().SelfMany(Claims.TeamIdsClaim);
            }));

        var record = new Record { ID = 30, CompanyID = 4, TeamID = 12 };

        return subjects
            .Where(subject => screenPermission(subject, access) && repository.CanAccessAs(record, access, subject.TypeAuth, subject))
            .Select(subject => subject.UserId)
            .ToList();
    }

    private static bool UsersScreen(IdentityAccessSubject subject, Access access) => subject.TypeAuth.Can(ShiftIdentityActions.Users, access);

    [Fact]
    public async Task Subjects_passed_to_CanAccessAs_give_the_answers_of_the_old_lookup()
    {
        var subjects = (await ResolveAsync(Source(), 1, 2)).Eligible;

        // The old lookup's answers on these fixtures: user 1 only, and only for Read.
        Assert.Equal(new long[] { 1 }, await WhoCanAsync(subjects, Access.Read, UsersScreen));
        Assert.Empty(await WhoCanAsync(subjects, Access.Write, UsersScreen));
        Assert.Empty(await WhoCanAsync(subjects, Access.Delete, UsersScreen));

        // A dynamic screen permission reads the candidate's own team claims.
        Assert.Equal(new long[] { 1 }, await WhoCanAsync(subjects, Access.Read, (subject, access) =>
            subject.TypeAuth.Can(ShiftIdentityActions.DataLevelAccess.Teams, access, "TeamDTO:12",
                subject.GetUser().FindAll(Claims.TeamIdsClaim).Select(claim => claim.Value).ToArray())));
    }

    [Theory]
    [InlineData("own tree")]
    [InlineData("named tree")]
    public async Task The_screen_permission_and_the_data_grant_are_both_needed(string removed)
    {
        var user = ActiveUser(1);
        if (removed == "own tree") user.AccessTree = null;
        else user.AccessTreeIDs = [];

        var subjects = (await ResolveAsync(Source(user), 1)).Eligible;

        Assert.Single(subjects);
        Assert.Empty(await WhoCanAsync(subjects, Access.Read, UsersScreen));
    }

    [Fact]
    public async Task A_change_is_seen_from_the_next_scope()
    {
        var source = Source();
        Assert.Equal(new long[] { 1 }, await WhoCanAsync((await ResolveAsync(source, 1, 2)).Eligible, Access.Read, UsersScreen));

        // Removing the assignment rewrites the user document with an empty list.
        var unassigned = ActiveUser(1);
        unassigned.AccessTreeIDs = [];
        source.Seed(unassigned, ActiveUser(2, company: 5));
        await source.RefreshAsync(IdentityReferenceFamily.Users, Ct);
        Assert.Empty(await WhoCanAsync((await ResolveAsync(source, 1, 2)).Eligible, Access.Read, UsersScreen));

        // Removing user 1 from team 12 rewrites the team document.
        source.Seed(ActiveUser(1), ActiveUser(2, company: 5));
        source.Seed(Team(11, 1), Team(12, 2));
        await source.RefreshAsync(IdentityReferenceFamily.Users, Ct);
        await source.RefreshAsync(IdentityReferenceFamily.Teams, Ct);
        Assert.Empty(await WhoCanAsync((await ResolveAsync(source, 1, 2)).Eligible, Access.Read, UsersScreen));

        // Editing the named tree revokes its grants.
        source.Seed(Team(11, 1), Team(12, 1, 2));
        source.Seed(new AccessTreeModel { id = "20", Name = "Revoked", Tree = "{}" });
        await source.RefreshAsync(IdentityReferenceFamily.Teams, Ct);
        await source.RefreshAsync(IdentityReferenceFamily.AccessTrees, Ct);
        Assert.Empty(await WhoCanAsync((await ResolveAsync(source, 1, 2)).Eligible, Access.Read, UsersScreen));
    }

    // ---- NotEligible ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("deleted", IdentitySubjectNotEligibleReason.Deleted)]
    [InlineData("inactive", IdentitySubjectNotEligibleReason.Inactive)]
    [InlineData("no company", IdentitySubjectNotEligibleReason.NoSignInScope)]
    [InlineData("no branch", IdentitySubjectNotEligibleReason.NoSignInScope)]
    [InlineData("no region", IdentitySubjectNotEligibleReason.NoSignInScope)]
    [InlineData("branch without city", IdentitySubjectNotEligibleReason.NoSignInScope)]
    public async Task A_user_that_sign_in_refuses_is_not_eligible(string state, IdentitySubjectNotEligibleReason reason)
    {
        var user = ActiveUser(2);
        var source = Source(ActiveUser(1), user);

        switch (state)
        {
            case "deleted": user.IsDeleted = true; break;
            case "inactive": user.IsActive = false; break;
            case "no company": user.CompanyID = null; break;
            case "no branch": user.CompanyBranchID = null; break;
            case "no region": user.RegionID = null; break;
            case "branch without city":
                user.CompanyBranchID = 18;
                source.Seed(new CompanyBranchModel { id = "8", CityID = 7 }, new CompanyBranchModel { id = "18" });
                break;
        }

        var resolution = await ResolveAsync(source, 1, 2);

        Assert.Equal(reason, resolution.NotEligible[2]);
        Assert.Equal(new long[] { 1 }, resolution.Eligible.Select(subject => subject.UserId));
        Assert.Empty(resolution.Unresolved);
    }

    // ---- Unresolved -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("no document", IdentitySubjectUnresolvedReason.UserMissing)]
    [InlineData("no IsActive", IdentitySubjectUnresolvedReason.UserOutdated)]
    [InlineData("no AccessTreeIDs", IdentitySubjectUnresolvedReason.UserOutdated)]
    [InlineData("missing company", IdentitySubjectUnresolvedReason.ReferenceMissing)]
    [InlineData("missing branch", IdentitySubjectUnresolvedReason.ReferenceMissing)]
    [InlineData("missing named tree", IdentitySubjectUnresolvedReason.ReferenceMissing)]
    [InlineData("empty named tree", IdentitySubjectUnresolvedReason.AccessTreeInvalid)]
    [InlineData("named tree does not parse", IdentitySubjectUnresolvedReason.AccessTreeInvalid)]
    [InlineData("own tree does not parse", IdentitySubjectUnresolvedReason.AccessTreeInvalid)]
    public async Task One_incomplete_user_is_unresolved_and_the_others_stay_eligible(string state, IdentitySubjectUnresolvedReason reason)
    {
        var user = ActiveUser(2, company: 5);
        var source = Source(ActiveUser(1), user);

        switch (state)
        {
            case "no document": source.Seed(ActiveUser(1)); break;
            case "no IsActive": user.IsActive = null; break;
            case "no AccessTreeIDs": user.AccessTreeIDs = null; break;
            case "missing company": user.CompanyID = 6; break;
            case "missing branch": user.CompanyBranchID = 18; break;
            case "missing named tree": user.AccessTreeIDs = [20, 21]; break;
            case "empty named tree":
                user.AccessTreeIDs = [20, 21];
                source.Seed(new AccessTreeModel { id = "20", Name = "Scope", Tree = ScopeGrant }, new AccessTreeModel { id = "21", Name = "Empty", Tree = " " });
                break;
            case "named tree does not parse":
                user.AccessTreeIDs = [20, 21];
                source.Seed(new AccessTreeModel { id = "20", Name = "Scope", Tree = ScopeGrant }, new AccessTreeModel { id = "21", Name = "Broken", Tree = "{" });
                break;
            case "own tree does not parse": user.AccessTree = "{"; break;
        }

        var resolution = await ResolveAsync(source, 1, 2);

        Assert.Equal(reason, resolution.Unresolved[2].Reason);
        Assert.Equal(new long[] { 1 }, resolution.Eligible.Select(subject => subject.UserId));
        Assert.Empty(resolution.NotEligible);
    }

    [Theory]
    [InlineData("named tree does not parse", "Access tree 21 does not parse.")]
    [InlineData("own tree does not parse", "User 2's own access tree does not parse.")]
    public async Task The_detail_names_the_tree_that_does_not_parse(string state, string detail)
    {
        var user = ActiveUser(2);
        var source = Source(ActiveUser(1), user);
        if (state == "own tree does not parse") user.AccessTree = "{";
        else
        {
            user.AccessTreeIDs = [20, 21];
            source.Seed(new AccessTreeModel { id = "20", Name = "Scope", Tree = ScopeGrant }, new AccessTreeModel { id = "21", Name = "Broken", Tree = "{" });
        }

        Assert.Equal(detail, (await ResolveAsync(source, 2)).Unresolved[2].Detail);
    }

    [Fact]
    public async Task A_team_without_its_member_list_leaves_every_user_unresolved_except_those_sign_in_refuses()
    {
        var deleted = ActiveUser(3);
        deleted.IsDeleted = true;
        var source = Source(ActiveUser(1), ActiveUser(2, company: 5), deleted);
        source.Seed(Team(11, 1), new TeamModel { id = "12", Name = "Written before the list existed" });

        var resolution = await ResolveAsync(source, 1, 2, 3);

        Assert.Empty(resolution.Eligible);
        Assert.Equal(IdentitySubjectUnresolvedReason.TeamMembersUnknown, resolution.Unresolved[1].Reason);
        Assert.Equal(IdentitySubjectUnresolvedReason.TeamMembersUnknown, resolution.Unresolved[2].Reason);
        Assert.Contains("Team 12", resolution.Unresolved[1].Detail);
        Assert.Equal(IdentitySubjectNotEligibleReason.Deleted, resolution.NotEligible[3]);
    }

    // ---- Missing documents are read again once ---------------------------------------------------------------------

    [Fact]
    public async Task A_missing_reference_is_read_again_once_and_then_reported()
    {
        // Two users refer to company 6, which has no document.
        var first = ActiveUser(1, company: 6);
        var second = ActiveUser(2, company: 6);
        var source = Source(first, second);

        var resolution = await ResolveAsync(source, 1, 2);

        Assert.Equal(IdentitySubjectUnresolvedReason.ReferenceMissing, resolution.Unresolved[1].Reason);
        Assert.Equal("Company 6 has no document.", resolution.Unresolved[2].Detail);
        // Two user reads and one read of company 6, shared by both users.
        Assert.Equal(3, source.ItemReads);
    }

    [Fact]
    public async Task A_reference_written_after_its_family_was_read_is_found_by_the_second_read()
    {
        var source = Source(ActiveUser(1), ActiveUser(2, company: 6));
        await source.GetCompaniesAsync(IdentityLifecycleFilter.All, Ct);
        source.Seed(new CompanyModel { id = "4" }, new CompanyModel { id = "5" }, new CompanyModel { id = "6" });

        var resolution = await ResolveAsync(source, 1, 2);

        Assert.Equal(new long[] { 1, 2 }, resolution.Eligible.Select(subject => subject.UserId));
    }

    [Fact]
    public async Task A_missing_user_is_read_again_once_and_then_reported()
    {
        var source = Source(ActiveUser(1));

        var resolution = await ResolveAsync(source, 1, 2);

        Assert.Equal(IdentitySubjectUnresolvedReason.UserMissing, resolution.Unresolved[2].Reason);
        Assert.Equal("User 2 has no document.", resolution.Unresolved[2].Detail);
        // User 1, user 2, and user 2 again.
        Assert.Equal(3, source.ItemReads);
    }

    // ---- Reuse within a scope ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Within_a_scope_a_user_is_prepared_once_and_a_new_scope_prepares_again()
    {
        var source = Source();
        var resolver = Resolver(source);

        var first = (await resolver.ResolveAsync([1, 2], Ct)).Eligible[0];
        var again = (await resolver.ResolveAsync([1], Ct)).Eligible[0];
        var parallel = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => resolver.ResolveAsync([2, 1, 1], Ct)));

        Assert.Same(first, again);
        Assert.All(parallel, resolution => Assert.Same(first, resolution.Eligible[0]));

        var nextScope = (await Resolver(source).ResolveAsync([1], Ct)).Eligible[0];
        Assert.NotSame(first, nextScope);
    }

    [Fact]
    public async Task Parallel_calls_in_a_new_scope_prepare_each_user_once()
    {
        var source = Source();
        var resolver = Resolver(source);
        source.Gate = new TaskCompletionSource();

        // Every call waits on the same reads, so all of them are in flight at once.
        var calls = Enumerable.Range(0, 8).Select(_ => resolver.ResolveAsync([1, 2], Ct)).ToArray();
        source.Gate.SetResult();
        var resolutions = await Task.WhenAll(calls);

        Assert.All(resolutions, resolution => Assert.Same(resolutions[0].Eligible[0], resolution.Eligible[0]));
        Assert.All(resolutions, resolution => Assert.Same(resolutions[0].Eligible[1], resolution.Eligible[1]));
        // One read per user: each user was prepared once.
        Assert.Equal(2, source.ItemReads);
    }

    [Fact]
    public async Task A_failed_read_is_not_kept_so_the_next_call_reads_again()
    {
        var source = Source();
        var resolver = Resolver(source);
        source.FailNextRead = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync([1], Ct));

        Assert.Equal(new long[] { 1 }, (await resolver.ResolveAsync([1], Ct)).Eligible.Select(subject => subject.UserId));
    }

    // ---- Registration ----------------------------------------------------------------------------------------------

    [Fact]
    public void The_resolver_is_scoped_and_reads_the_scopes_reference_source()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new StubCosmosClient());
        services.AddScoped(_ => HostTypeAuth());
        services.AddSingleton<IHashIdService, EncodedIds>();
        services.AddIdentitySubjectResolver<StubCosmosClient>();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var resolver = first.ServiceProvider.GetRequiredService<IdentitySubjectResolver>();

        Assert.Same(resolver, first.ServiceProvider.GetRequiredService<IdentitySubjectResolver>());
        Assert.NotSame(resolver, second.ServiceProvider.GetRequiredService<IdentitySubjectResolver>());
        Assert.IsType<CosmosIdentityReferenceSource<StubCosmosClient>>(first.ServiceProvider.GetRequiredService<IIdentityReferenceSource>());
    }

    // ---- The replication maps that produce these documents ---------------------------------------------------------

    [Fact]
    public void Generated_replication_maps_list_assignments_in_assignment_order_and_members_in_ascending_order()
    {
        var services = new ServiceCollection().AddShiftIdentityReplicationMapper();
        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<ShiftMapper.IMapper>();
        // Tree 31 was assigned first (assignment 100). Neither the navigation's order nor the tree IDs decide the list.
        var user = mapper.Map<User, UserModel>(new User
        {
            ID = 1, IsActive = true, AccessTree = SystemGrant, EmailVerified = true,
            AccessTrees = [new UserAccessTree { ID = 101, UserID = 1, AccessTreeID = 20 }, new UserAccessTree { ID = 100, UserID = 1, AccessTreeID = 31 }],
        });
        Assert.True(user.IsActive);
        Assert.Equal(SystemGrant, user.AccessTree);
        Assert.Equal(new long[] { 31, 20 }, user.AccessTreeIDs);
        Assert.True(user.EmailVerified);
        Assert.False(user.PhoneVerified);
        // A written document always carries the list, so an empty one means "no assigned tree", never "unknown".
        Assert.Equal(Array.Empty<long>(), mapper.Map<User, UserModel>(new User { ID = 2 }).AccessTreeIDs);
        var team = mapper.Map<Team, TeamModel>(new Team
        {
            ID = 12, Name = "Team",
            TeamUsers = [new TeamUser { ID = 201, UserID = 7, TeamID = 12 }, new TeamUser { ID = 200, UserID = 1, TeamID = 12 }],
        });
        Assert.Equal(new long[] { 1, 7 }, team.UserIDs);
        var tree = mapper.Map<AccessTree, AccessTreeModel>(new AccessTree { ID = 20, Tree = ScopeGrant, Name = "Scope" });
        Assert.Equal("20", tree.id);
        Assert.Equal(ScopeGrant, tree.Tree);
    }

    // ---- Test doubles ----------------------------------------------------------------------------------------------

    private sealed class NoUser : ICurrentUserProvider
    {
        public ClaimsPrincipal? GetUser() => null;
    }

    // The record check above uses a v2 declaration, so the legacy rules must never run.
    private sealed class NoLegacyRules : IDefaultDataLevelAccess
    {
        public List<long?>? GetAccessibleCountries() => throw new NotSupportedException();
        public List<long?>? GetAccessibleRegions() => throw new NotSupportedException();
        public List<long?>? GetAccessibleCompanies() => throw new NotSupportedException();
        public List<long?>? GetAccessibleBranches() => throw new NotSupportedException();
        public List<long?>? GetAccessibleBrands() => throw new NotSupportedException();
        public List<long?>? GetAccessibleCities() => throw new NotSupportedException();
        public List<long?>? GetAccessibleTeams() => throw new NotSupportedException();
        public IQueryable<T> ApplyDefaultCountryFilter<T>(IQueryable<T> query) where T : IEntityHasCountry<T> => throw new NotSupportedException();
        public IQueryable<T> ApplyDefaultRegionFilter<T>(IQueryable<T> query) where T : IEntityHasRegion<T> => throw new NotSupportedException();
        public IQueryable<T> ApplyDefaultCompanyFilter<T>(IQueryable<T> query) where T : IEntityHasCompany<T> => throw new NotSupportedException();
        public IQueryable<T> ApplyDefaultBranchFilter<T>(IQueryable<T> query) where T : IEntityHasCompanyBranch<T> => throw new NotSupportedException();
        public IQueryable<T> ApplyDefaultBrandFilter<T>(IQueryable<T> query) where T : IEntityHasBrand<T> => throw new NotSupportedException();
        public IQueryable<T> ApplyDefaultCityFilter<T>(IQueryable<T> query) where T : IEntityHasCity<T> => throw new NotSupportedException();
        public IQueryable<T> ApplyDefaultTeamFilter<T>(IQueryable<T> query) where T : IEntityHasTeam<T> => throw new NotSupportedException();
        public bool HasDefaultDataLevelAccess<T>(DefaultDataLevelAccessOptions options, T? entity, Access access) where T : ShiftEntity<T>, new()
            => throw new NotSupportedException();
        public IQueryable<T> ApplyDefaultDataLevelFilters<T>(DefaultDataLevelAccessOptions options, IQueryable<T> query) where T : notnull
            => throw new NotSupportedException();
    }

    private sealed class EncodedIds : IHashIdService
    {
        public string Encode(long id, Type type) => type.Name + ":" + id;
        public long Decode(string key, Type type)
        {
            Assert.StartsWith(type.Name + ":", key);
            return long.Parse(key[(type.Name.Length + 1)..]);
        }
        public string Encode<T>(long id) => Encode(id, typeof(T));
        public long Decode<T>(string key) => Decode(key, typeof(T));
        public bool IsConfigurationRegistered(string name) => throw new NotImplementedException();
        public bool IsAcceptUnencodedIds(string? name) => false;
        public string Encode(long id, JsonHashIdConverterAttribute attribute) => throw new NotImplementedException();
        public long Decode(string key, JsonHashIdConverterAttribute attribute) => throw new NotImplementedException();
        public ShiftEntityHashId? GetHasherFor(JsonHashIdConverterAttribute attribute) => throw new NotImplementedException();
    }

    // A host action whose merged value depends on the order of the trees: a text action without a comparer.
    [ActionTree("Ordered", "A text action without a comparer")]
    public class OrderedActions
    {
        public readonly static TextAction Greeting = new("Greeting");
    }
}
