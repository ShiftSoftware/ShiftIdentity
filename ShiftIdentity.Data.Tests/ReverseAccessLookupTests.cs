using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using ShiftIdentity.Data.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Core.DataLevelAccess;
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
using Xunit;
using Claims = ShiftSoftware.ShiftEntity.Core.Constants;

namespace ShiftIdentity.Data.Tests;

public class ReverseAccessLookupTests
{
    private static readonly Type[] Actions = [typeof(ShiftIdentityActions)];
    private const string SystemGrant = """{"ShiftIdentityActions":{"Users":["Read"]}}""";
    private static readonly string ScopeGrant = """
        {"ShiftIdentityActions":{"DataLevelAccess":{
            "Companies":{"@self@":["Read"]},"Teams":{"@self@":["Read"]}
        }}}
        """.Replace("@self@", TypeAuthContext.SelfReferenceKey);

    private sealed record Row(long? CompanyID, long? TeamID);
    private static DataLevelAccessPolicy<Row> Policy()
    {
        var builder = new DataLevelAccessBuilder<Row>();
        builder.On(ShiftIdentityActions.DataLevelAccess.Companies).Key(row => row.CompanyID)
            .HashId<CompanyDTO>().Self(Claims.CompanyIdClaim);
        builder.On(ShiftIdentityActions.DataLevelAccess.Teams).Key(row => row.TeamID)
            .HashId<TeamDTO>().SelfMany(Claims.TeamIdsClaim);
        return new(builder);
    }

    private static UserModel ActiveUser(string id = "1", long company = 4) => new()
    {
        id = id, IsActive = true, Username = "user-" + id, FullName = "User " + id,
        CompanyID = company, CompanyBranchID = 8, RegionID = 9, CountryID = 10,
        AccessTree = SystemGrant,
    };

    private static StubIdentityReferenceSource Source()
    {
        var source = new StubIdentityReferenceSource();
        source.Seed(ActiveUser(), ActiveUser("2", 5));
        source.Seed(new CompanyModel { id = "4" }, new CompanyModel { id = "5" });
        source.Seed(new CompanyBranchModel { id = "8", CityID = 7 });
        source.Seed(new AccessTreeModel { id = "20", Name = "Scope", Tree = ScopeGrant });
        source.Seed(new UserAccessTreeModel { id = "100", UserID = 1, AccessTreeID = 20 },
            new UserAccessTreeModel { id = "101", UserID = 2, AccessTreeID = 20 });
        source.Seed(new TeamUserModel { id = "200", UserID = 1, TeamID = 11 },
            new TeamUserModel { id = "201", UserID = 1, TeamID = 12 },
            new TeamUserModel { id = "202", UserID = 2, TeamID = 12 });
        return source;
    }

    private static IdentityReverseAccessLookup Lookup(StubIdentityReferenceSource source)
        => new(source, source, new EncodedIds());

    private static Task<IReadOnlyList<string>> Find(IdentityReverseAccessLookup lookup, Access access = Access.Read)
        => lookup.FindUsersAsync(new Row(4, 12), access, Policy(),
            (subject, operation) => subject.TypeAuth.Can(ShiftIdentityActions.Users, operation), Actions,
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task Merged_grants_and_hashed_self_claims_match_only_the_correct_user()
    {
        var source = Source();
        var lookup = Lookup(source);
        Assert.Equal(new[] { "1" }, await Find(lookup));
        Assert.Empty(await Find(lookup, Access.Write));
        Assert.Empty(await Find(lookup, Access.Delete));

        var subject = (await lookup.GetSubjectsAsync(Actions, TestContext.Current.CancellationToken))[0];
        Assert.Equal("UserDTO:1", subject.GetUser().FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("CityDTO:7", subject.GetUser().FindFirst(Claims.CityIdClaim)?.Value);
        Assert.Equal("CountryDTO:10", subject.GetUser().FindFirst(Claims.CountryIdClaim)?.Value);
        Assert.Equal(new[] { "TeamDTO:11", "TeamDTO:12" }, subject.GetUser().FindAll(Claims.TeamIdsClaim).Select(x => x.Value));
    }

    [Fact]
    public async Task Dynamic_system_actions_receive_the_candidates_own_self_ids()
    {
        var lookup = Lookup(Source());
        var result = await lookup.FindUsersAsync(new Row(4, 12), Access.Read, Policy(),
            (subject, operation) => subject.TypeAuth.Can(ShiftIdentityActions.DataLevelAccess.Teams,
                operation, "TeamDTO:12", subject.GetUser().FindAll(Claims.TeamIdsClaim).Select(x => x.Value).ToArray()),
            Actions, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "1" }, result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Both_system_and_data_grants_are_required(bool removeSystem)
    {
        var source = Source();
        if (removeSystem)
        {
            var user = ActiveUser();
            user.AccessTree = null;
            source.Seed(user);
        }
        else source.Seed<UserAccessTreeModel>();
        Assert.Empty(await Find(Lookup(source)));
    }

    [Fact]
    public async Task Assignment_removal_membership_removal_and_tree_edits_are_visible_after_refresh()
    {
        var source = Source();
        var lookup = Lookup(source);
        Assert.Equal(new[] { "1" }, await Find(lookup));
        source.Seed<UserAccessTreeModel>();
        await source.RefreshAuthorizationAsync(TestContext.Current.CancellationToken);
        Assert.Empty(await Find(lookup));

        source.Seed(new UserAccessTreeModel { id = "100", UserID = 1, AccessTreeID = 20 });
        source.Seed(new TeamUserModel { id = "200", UserID = 1, TeamID = 11 });
        await source.RefreshAuthorizationAsync(TestContext.Current.CancellationToken);
        Assert.Empty(await Find(lookup));

        source.Seed(new TeamUserModel { id = "201", UserID = 1, TeamID = 12 });
        await source.RefreshAuthorizationAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "1" }, await Find(lookup));
        source.Seed(new AccessTreeModel { id = "20", Name = "Revoked", Tree = "{}" });
        await source.RefreshAuthorizationAsync(TestContext.Current.CancellationToken);
        Assert.Empty(await Find(lookup));
    }

    [Fact]
    public async Task Disabled_and_deleted_users_are_excluded_but_old_documents_are_reported()
    {
        var source = Source();
        var disabled = ActiveUser(); disabled.IsActive = false;
        var deleted = ActiveUser("2"); deleted.IsDeleted = true;
        source.Seed(disabled, deleted);
        Assert.Empty(await Find(Lookup(source)));
        var old = ActiveUser(); old.IsActive = null;
        source.Seed(old);
        await source.RefreshAsync(IdentityReferenceFamily.Users, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Find(Lookup(source)));
    }

    [Theory]
    [InlineData("tree")]
    [InlineData("branch")]
    [InlineData("company")]
    public async Task Missing_referenced_documents_do_not_produce_a_partial_audience(string missing)
    {
        var source = Source();
        if (missing == "tree") source.Seed<AccessTreeModel>();
        if (missing == "branch") source.Seed<CompanyBranchModel>();
        if (missing == "company") source.Seed<CompanyModel>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Find(Lookup(source)));
    }

    [Fact]
    public async Task Named_tree_lifecycle_matches_current_Identity_claim_issuance()
    {
        var source = Source();
        source.Seed(new AccessTreeModel { id = "20", Name = "Still assigned", Tree = ScopeGrant, IsDeleted = true });
        // Ordinary token issuance includes an assigned tree without checking its IsDeleted flag.
        // An assignment removal or a tree-content edit is what changes these effective grants.
        Assert.Equal(new[] { "1" }, await Find(Lookup(source)));
    }

    [Fact]
    public void Generated_replication_maps_preserve_join_ids_and_authorization_fields()
    {
        var services = new ServiceCollection().AddShiftIdentityReplicationMapper();
        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<ShiftMapper.IMapper>();
        var user = mapper.Map<User, UserModel>(new User { ID = 1, IsActive = true, AccessTree = SystemGrant });
        Assert.True(user.IsActive);
        Assert.Equal(SystemGrant, user.AccessTree);
        var assignment = mapper.Map<UserAccessTree, UserAccessTreeModel>(new UserAccessTree
            { ID = 100, UserID = 1, AccessTreeID = 20, IsDeleted = true });
        Assert.Equal("100", assignment.id);
        Assert.Equal(1, assignment.UserID);
        Assert.Equal(20, assignment.AccessTreeID);
        Assert.True(assignment.IsDeleted);
        var membership = mapper.Map<TeamUser, TeamUserModel>(new TeamUser { ID = 200, UserID = 1, TeamID = 12 });
        Assert.Equal("200", membership.id);
        Assert.Equal(12, membership.TeamID);
        var tree = mapper.Map<AccessTree, AccessTreeModel>(new AccessTree { ID = 20, Tree = ScopeGrant, Name = "Scope" });
        Assert.Equal("20", tree.id);
        Assert.Equal(ScopeGrant, tree.Tree);
    }

    [Fact]
    public void Cosmos_registration_shares_the_reference_source_and_auth_reader_within_the_scope()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new StubCosmosClient());
        services.AddIdentityReverseAccessLookup<StubCosmosClient>();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        Assert.Same(scope.ServiceProvider.GetRequiredService<IIdentityReferenceSource>(),
            scope.ServiceProvider.GetRequiredService<IIdentityAuthorizationSource>());
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
}
