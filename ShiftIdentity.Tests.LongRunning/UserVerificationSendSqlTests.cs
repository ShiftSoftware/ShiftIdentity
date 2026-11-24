using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// The verification send of a user saved twice in one transaction, on SQL Server, where a save that fails inside the
/// caller's transaction is rolled back to SQL Server's own savepoint: one link goes, the one asked last, and when the
/// later save fails and the caller commits all the same, the earlier one goes. ShiftIdentity.Data.Tests holds the same
/// on SQLite. The class has a database of its own, because it adds users.
/// </summary>
public sealed class UserVerificationSendSqlTests(SqlIdentityFixture fixture) : IClassFixture<SqlIdentityFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Refuses, before anything is written, a save of a user whose name starts with "refused".</summary>
    public sealed class RefusesTheUser : IBeforeSaveHandler<User>
    {
        public ValueTask BeforeSaveAsync(PendingRows<User> rows, CancellationToken cancellationToken) =>
            rows.Any(row => row.Entity.Username.StartsWith("refused")) ? throw new InvalidOperationException("The user is refused.") : ValueTask.CompletedTask;
    }

    /// <summary>Fails, after the write, a save of a user whose name starts with "fails". The save is undone.</summary>
    public sealed class FailsAfterTheWrite : IAfterSaveHandler<User>
    {
        public ValueTask AfterSaveAsync(SavedRows<User> rows, CancellationToken cancellationToken) =>
            rows.Any(row => row.Entity.Username.StartsWith("fails")) ? throw new InvalidOperationException("The save failed after its write.") : ValueTask.CompletedTask;
    }

    // What a user repository needs, with the context on the fixture's database and its handlers from these services.
    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddSingleton<IHashIdService>(new HashIdService(Options.Create(new ShiftEntityOptions())));
        services.AddSingleton(Substitute.For<ICurrentUserProvider>());
        services.AddScoped<IdentityClaimProvider>();
        services.AddSingleton(Substitute.For<IDefaultDataLevelAccess>());
        services.AddSaveHandler<RefusesTheUser>().AddSaveHandler<FailsAfterTheWrite>();
        services.AddScoped(provider => fixture.CreateContext(provider));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static (ShiftIdentityDbContext Db, UserRepository Repository) Repository(IServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<ShiftIdentityDbContext>();
        var repository = new UserRepository(db, new ShiftIdentityDefaultDataLevelAccessOptions(),
            new ShiftIdentityLocalizer(scope.ServiceProvider, typeof(UserVerificationSendSqlTests)), new ShiftIdentityConfiguration());

        return (db, repository);
    }

    private static User AUser(string name) => new()
    {
        Username = name + "-" + Guid.NewGuid().ToString("N"), FullName = name, IsActive = true, PasswordHash = [1], Salt = [2],
    };

    [Fact]
    public async Task Two_repository_saves_of_one_user_in_one_transaction_each_with_an_ask_send_once_the_later_ask_to_the_committed_address()
    {
        await using var services = Services();
        using var scope = services.CreateScope();
        var (db, repository) = Repository(scope);

        var sent = new List<string>();
        var user = AUser("twice");
        user.Email = $"first-{Guid.NewGuid():N}@example.invalid";

        Func<Task> Link(string ask) => () =>
        {
            sent.Add($"{ask}, to {user.Email}");
            return Task.CompletedTask;
        };

        await using (var transaction = await db.Database.BeginTransactionAsync(Ct))
        {
            db.Users.Add(user);
            repository.SendWhenCommitted(user, Link("the earlier ask"));
            await repository.SaveChangesAsync();

            user.Email = $"second-{Guid.NewGuid():N}@example.invalid";
            repository.SendWhenCommitted(user, Link("the later ask"));
            await repository.SaveChangesAsync();

            Assert.Empty(sent);
            await transaction.CommitAsync(Ct);
        }

        Assert.Equal([$"the later ask, to {user.Email}"], sent);

        await using var check = fixture.CreateContext();
        Assert.Equal(user.Email, (await check.Users.AsNoTracking().SingleAsync(x => x.ID == user.ID, Ct)).Email);
    }

    [Theory]
    [InlineData("refused before its write")]
    [InlineData("fails after its write")]
    public async Task When_the_later_save_of_the_user_fails_and_the_caller_commits_all_the_same_the_earlier_ask_is_sent(string how)
    {
        await using var services = Services();
        using var scope = services.CreateScope();
        var (db, repository) = Repository(scope);

        var sent = new List<string>();
        var user = AUser("later-fails");
        var name = user.Username;

        await using (var transaction = await db.Database.BeginTransactionAsync(Ct))
        {
            db.Users.Add(user);
            repository.SendWhenCommitted(user, () =>
            {
                sent.Add("the earlier ask");
                return Task.CompletedTask;
            });
            await repository.SaveChangesAsync();

            user.Username = (how == "refused before its write" ? "refused-" : "fails-") + Guid.NewGuid().ToString("N");
            repository.SendWhenCommitted(user, () =>
            {
                sent.Add("the later ask");
                return Task.CompletedTask;
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveChangesAsync());

            // The caller swallows the failure and commits what was saved before it.
            await transaction.CommitAsync(Ct);
        }

        Assert.Equal(["the earlier ask"], sent);

        await using var check = fixture.CreateContext();
        Assert.Equal(name, (await check.Users.AsNoTracking().SingleAsync(x => x.ID == user.ID, Ct)).Username);
    }
}
