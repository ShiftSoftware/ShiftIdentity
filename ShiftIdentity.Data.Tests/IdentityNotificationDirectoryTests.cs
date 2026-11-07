using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShiftIdentity.Data.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Core.Notifications;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Data.Authorization;
using ShiftSoftware.ShiftIdentity.Data.IdentityReference;
using ShiftSoftware.ShiftIdentity.Data.Notifications;
using ShiftSoftware.TypeAuth.Core;
using Xunit;

namespace ShiftIdentity.Data.Tests;

/// <summary>What the notification send path reads about people and branches, from the replicated documents.</summary>
public class IdentityNotificationDirectoryTests
{
    private const string Grant = """{"ShiftIdentityActions":{"Users":["Read"]}}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static UserModel User(long id) => new()
    {
        id = id.ToString(),
        IsActive = true,
        Username = "user-" + id,
        FullName = "User " + id,
        Email = $"user{id}@example.com",
        EmailVerified = true,
        CompanyID = 4,
        CompanyBranchID = 8,
        RegionID = 9,
        AccessTree = Grant,
        AccessTreeIDs = [],
    };

    private static StubIdentityReferenceSource Source(params UserModel[] users)
    {
        var source = new StubIdentityReferenceSource();
        source.Seed(users);
        source.Seed(new CompanyModel { id = "4" });
        source.Seed(new CompanyBranchModel { id = "8", CityID = 7 });
        source.Seed<AccessTreeModel>();
        source.Seed<TeamModel>();
        return source;
    }

    private static IdentityNotificationDirectory Directory(IIdentityReferenceSource source)
    {
        var typeAuth = new TypeAuthContextBuilder().AddActionTree<ShiftIdentityActions>().Build();
        var hashIds = new HashIdService(Options.Create(new ShiftEntityOptions()));

        return new(new IdentitySubjectResolver(source, typeAuth, hashIds), source);
    }

    [Fact]
    public async Task AUserWhoCouldSignIn_ComesBackWithGrantsClaimsAndEmail()
    {
        var lookup = await Directory(Source(User(1))).LookUpUsersAsync([1], Ct);

        var user = Assert.Single(lookup.Eligible);

        Assert.Equal(1, user.UserID);
        Assert.Equal("user1@example.com", user.Email);
        Assert.True(user.EmailVerified);
        Assert.True(user.TypeAuth.CanRead(ShiftIdentityActions.Users));
        Assert.True(user.Claims.GetUser()!.Identity!.IsAuthenticated);
        Assert.Empty(lookup.NotEligible);
        Assert.Empty(lookup.Unresolved);
    }

    [Fact]
    public async Task AnUnverifiedEmail_IsReported_AsNotVerified()
    {
        var user = User(1);
        user.EmailVerified = false;

        var lookup = await Directory(Source(user)).LookUpUsersAsync([1], Ct);

        Assert.False(Assert.Single(lookup.Eligible).EmailVerified);
    }

    [Fact]
    public async Task AUserWhoCouldNotSignIn_IsNotEligible_WithTheReason()
    {
        var user = User(1);
        user.IsActive = false;

        var lookup = await Directory(Source(user)).LookUpUsersAsync([1], Ct);

        Assert.Empty(lookup.Eligible);
        Assert.Equal("Inactive", lookup.NotEligible[1]);
    }

    [Fact]
    public async Task AUserWithNoDocument_IsUnresolved_WithTheReason()
    {
        var lookup = await Directory(Source()).LookUpUsersAsync([1], Ct);

        Assert.Empty(lookup.Eligible);
        Assert.Contains("UserMissing", lookup.Unresolved[1]);
    }

    [Fact]
    public async Task TeamMembers_ComeFromTheTeamDocument()
    {
        var source = Source();
        source.Seed(new TeamModel { id = "11", Name = "Team", UserIDs = [1, 2] });

        Assert.Equal([1, 2], await Directory(source).GetTeamMembersAsync(11, Ct));
    }

    [Fact]
    public async Task ATeamWithNoMemberList_OrNoDocument_IsNotKnown_AndNeverEmpty()
    {
        var source = Source();
        source.Seed(new TeamModel { id = "11", Name = "Team", UserIDs = null });

        Assert.Null(await Directory(source).GetTeamMembersAsync(11, Ct));
        Assert.Null(await Directory(source).GetTeamMembersAsync(12, Ct));
    }

    [Fact]
    public async Task ATeamWithNoMembers_IsKnownToBeEmpty()
    {
        var source = Source();
        source.Seed(new TeamModel { id = "11", Name = "Team", UserIDs = [] });

        Assert.Empty((await Directory(source).GetTeamMembersAsync(11, Ct))!);
    }

    [Fact]
    public async Task BranchEmails_ComeFromThatBranchsDocument()
    {
        var source = Source();
        source.Seed(
            new CompanyBranchModel { id = "8", Emails = [new TaggedTextDTO { Text = "a@example.com", Tag = "Sales" }] },
            new CompanyBranchModel { id = "9", Emails = [new TaggedTextDTO { Text = "b@example.com", Tag = "Sales" }] });

        var emails = await Directory(source).GetBranchEmailsAsync(8, Ct);

        Assert.Equal("a@example.com", Assert.Single(emails!).Text);
    }

    [Fact]
    public async Task ABranchWithNoDocument_HasNoEmails()
    {
        Assert.Null(await Directory(Source()).GetBranchEmailsAsync(99, Ct));
    }

    [Fact]
    public void TheRegistration_AddsTheDirectory_AndOnlyThat()
    {
        var services = new ServiceCollection();
        services.AddIdentityNotificationDirectory();

        var descriptor = Assert.Single(services);

        Assert.Equal(typeof(INotificationIdentityDirectory), descriptor.ServiceType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }
}
