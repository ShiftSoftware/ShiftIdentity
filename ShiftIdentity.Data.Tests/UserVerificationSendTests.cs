using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;
using Xunit;

namespace ShiftIdentity.Data.Tests;

/// <summary>
/// What the User form asks to send to a user is sent once the repository's save that writes that user is committed:
/// not when the form's hook asks, which is before the save, and not for a save that is refused, fails, is rolled back
/// or is switched off for it. Nothing about a send outlives that save. One send goes to a user for what commits
/// together, and every send of a commit is attempted. On SQLite, so the transactions are real.
/// </summary>
public class UserVerificationSendTests : IAsyncDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The identity model on SQLite. Left out: the tables SQLite cannot create, which these tests do not read.
    /// </summary>
    private sealed class IdentityDb(DbContextOptions<IdentityDb> options) : ShiftIdentityDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Ignore<CompanyCalendarBranch>();
            builder.Ignore<CompanyCalendar>();
            builder.Ignore<UserSecurityState>();
            builder.Ignore<AuthenticationOperation>();
            builder.Ignore<UserProviderLink>();
            builder.Ignore<AuthenticationPolicyState>();
            builder.Ignore<AuthenticationAuditEvent>();
            builder.Ignore<AuthThrottleBucket>();
            builder.Ignore<DeviceAuthorization>();
        }
    }

    private sealed class NoUser : ICurrentUserProvider
    {
        public System.Security.Claims.ClaimsPrincipal? GetUser() => null;
    }

    /// <summary>What a repository is built with. Its save, which is all these tests use, asks nothing of it.</summary>
    private sealed class NoDataLevelAccess : IDefaultDataLevelAccess
    {
        private static NotSupportedException NotHere() => new("These tests read no data-level access.");
        public List<long?>? GetAccessibleCountries() => throw NotHere();
        public List<long?>? GetAccessibleRegions() => throw NotHere();
        public List<long?>? GetAccessibleCompanies() => throw NotHere();
        public List<long?>? GetAccessibleBranches() => throw NotHere();
        public List<long?>? GetAccessibleBrands() => throw NotHere();
        public List<long?>? GetAccessibleCities() => throw NotHere();
        public List<long?>? GetAccessibleTeams() => throw NotHere();
        public IQueryable<T> ApplyDefaultCountryFilter<T>(IQueryable<T> query) where T : ShiftSoftware.ShiftEntity.Model.Flags.IEntityHasCountry<T> => throw NotHere();
        public IQueryable<T> ApplyDefaultRegionFilter<T>(IQueryable<T> query) where T : ShiftSoftware.ShiftEntity.Model.Flags.IEntityHasRegion<T> => throw NotHere();
        public IQueryable<T> ApplyDefaultCompanyFilter<T>(IQueryable<T> query) where T : ShiftSoftware.ShiftEntity.Model.Flags.IEntityHasCompany<T> => throw NotHere();
        public IQueryable<T> ApplyDefaultBranchFilter<T>(IQueryable<T> query) where T : ShiftSoftware.ShiftEntity.Model.Flags.IEntityHasCompanyBranch<T> => throw NotHere();
        public IQueryable<T> ApplyDefaultBrandFilter<T>(IQueryable<T> query) where T : ShiftSoftware.ShiftEntity.Model.Flags.IEntityHasBrand<T> => throw NotHere();
        public IQueryable<T> ApplyDefaultCityFilter<T>(IQueryable<T> query) where T : ShiftSoftware.ShiftEntity.Model.Flags.IEntityHasCity<T> => throw NotHere();
        public IQueryable<T> ApplyDefaultTeamFilter<T>(IQueryable<T> query) where T : ShiftSoftware.ShiftEntity.Model.Flags.IEntityHasTeam<T> => throw NotHere();
        public bool HasDefaultDataLevelAccess<T>(DefaultDataLevelAccessOptions options, T? entity, ShiftSoftware.TypeAuth.Core.Access access) where T : ShiftEntity<T>, new() => throw NotHere();
        public IQueryable<T> ApplyDefaultDataLevelFilters<T>(DefaultDataLevelAccessOptions options, IQueryable<T> query) where T : notnull => throw NotHere();
    }

    /// <summary>A localizer that answers each name with itself, for a repository that has nothing to translate here.</summary>
    private sealed class NamesAsTheyAre : IStringLocalizerFactory, IStringLocalizer
    {
        public IStringLocalizer Create(Type resourceSource) => this;
        public IStringLocalizer Create(string baseName, string location) => this;
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    /// <summary>Refuses, before anything is written, a save of a user named "refused".</summary>
    public sealed class RefusesTheUser : IBeforeSaveHandler<User>
    {
        public ValueTask BeforeSaveAsync(PendingRows<User> rows, CancellationToken cancellationToken) =>
            rows.Any(row => row.Entity.Username == "refused") ? throw new InvalidOperationException("The user is refused.") : ValueTask.CompletedTask;
    }

    /// <summary>Fails, after the write, a save of a user named "fails". The save is undone.</summary>
    public sealed class FailsAfterTheWrite : IAfterSaveHandler<User>
    {
        public ValueTask AfterSaveAsync(SavedRows<User> rows, CancellationToken cancellationToken) =>
            rows.Any(row => row.Entity.Username == "fails") ? throw new InvalidOperationException("The save failed after its write.") : ValueTask.CompletedTask;
    }

    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private ServiceProvider? provider;

    public async ValueTask DisposeAsync()
    {
        if (provider is not null)
            await provider.DisposeAsync();

        await connection.DisposeAsync();
    }

    // A host with what a user repository needs, on one SQLite connection.
    private void Start(Action<IServiceCollection>? more = null)
    {
        connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<IdentityDb>(options => options.UseSqlite(connection));
        services.AddSingleton<IStringLocalizerFactory, NamesAsTheyAre>();
        services.AddSingleton<IHashIdService>(new HashIdService(Options.Create(new ShiftEntityOptions())));
        services.AddScoped<ICurrentUserProvider, NoUser>();
        services.AddScoped<IdentityClaimProvider>();
        services.AddSingleton<IDefaultDataLevelAccess, NoDataLevelAccess>();
        more?.Invoke(services);

        provider = services.BuildServiceProvider(validateScopes: true);

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IdentityDb>().Database.EnsureCreated();
    }

    // One scope's context and its user repository.
    private (IdentityDb Db, UserRepository Repository, IServiceScope Scope) Scope()
    {
        var scope = provider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDb>();
        var repository = new UserRepository(db, new ShiftIdentityDefaultDataLevelAccessOptions(),
            new ShiftIdentityLocalizer(scope.ServiceProvider, typeof(UserVerificationSendTests)), new ShiftIdentityConfiguration());

        return (db, repository, scope);
    }

    private static User AUser(string name) => new() { Username = name, FullName = name, IsActive = true, PasswordHash = [1], Salt = [2] };

    private static Func<Task> Records(List<string> sent, string what) => () =>
    {
        sent.Add(what);
        return Task.CompletedTask;
    };

    // ── The send waits for the commit ───────────────────────────────────────────

    [Fact]
    public async Task The_send_runs_once_when_the_repositorys_save_that_writes_the_user_has_committed()
    {
        Start();
        var (db, repository, scope) = Scope();
        using var _ = scope;

        var sent = new List<string>();
        var user = AUser("alice");

        db.Users.Add(user);
        repository.SendWhenCommitted(user, () =>
        {
            sent.Add($"{user.Username}, with an ID: {user.ID > 0}, {db.Entry(user).State}");
            return Task.CompletedTask;
        });

        // Asked before the save, where the user has no ID and the save can still fail.
        Assert.Empty(sent);

        await repository.SaveChangesAsync();

        Assert.Equal(["alice, with an ID: True, Unchanged"], sent);

        // A later save of the same user sends nothing: nobody asked again.
        user.FullName = "Alice";
        await repository.SaveChangesAsync();
        user.FullName = "Alice A.";
        await db.SaveChangesAsync(Ct);

        Assert.Single(sent);
        Assert.Null(user.verificationSendOfThisSave);
    }

    [Fact]
    public async Task Inside_a_transaction_of_the_callers_it_waits_for_the_commit_and_a_rollback_sends_nothing_then_or_later()
    {
        Start();
        var (db, repository, scope) = Scope();
        using var _ = scope;

        var sent = new List<string>();

        var alice = AUser("alice");
        db.Users.Add(alice);
        repository.SendWhenCommitted(alice, Records(sent, "alice"));

        await using (var transaction = await db.Database.BeginTransactionAsync(Ct))
        {
            await repository.SaveChangesAsync();

            // Saved, and not committed: the caller may still roll back.
            Assert.Empty(sent);

            await transaction.CommitAsync(Ct);
        }

        Assert.Equal(["alice"], sent);

        var bob = AUser("bob");
        db.Users.Add(bob);
        repository.SendWhenCommitted(bob, Records(sent, "bob"));

        await using (var transaction = await db.Database.BeginTransactionAsync(Ct))
        {
            await repository.SaveChangesAsync();
            await transaction.RollbackAsync(Ct);
        }

        Assert.Equal(["alice"], sent);

        // The rollback left the context with a user it shows as saved: cleared, a later save of bob sends nothing.
        db.ChangeTracker.Clear();
        bob.ID = 0;
        db.Users.Add(bob);
        await repository.SaveChangesAsync();

        Assert.Equal(["alice"], sent);
    }

    [Fact]
    public async Task A_send_that_fails_leaves_the_user_saved_and_reaches_the_caller_as_a_failure_after_the_commit()
    {
        Start();
        var (db, repository, scope) = Scope();
        using var _ = scope;

        var user = AUser("alice");

        db.Users.Add(user);
        repository.SendWhenCommitted(user, () => throw new InvalidOperationException("The mail server refused the message."));

        var failure = await Assert.ThrowsAsync<AfterCommitException>(() => repository.SaveChangesAsync());

        Assert.Equal("The mail server refused the message.", Assert.Single(failure.Failures).Message);
        Assert.Equal(1, await db.Users.CountAsync(Ct));

        // The failed send is not tried again by a later save.
        user.FullName = "Alice";
        await repository.SaveChangesAsync();
    }

    [Fact]
    public async Task A_user_nobody_asked_to_send_to_is_saved_and_sends_nothing_and_asking_twice_sends_what_was_asked_last()
    {
        Start();
        var (db, repository, scope) = Scope();
        using var _ = scope;

        var sent = new List<string>();

        db.Users.Add(AUser("nobody asked"));
        await repository.SaveChangesAsync();

        var user = AUser("alice");
        db.Users.Add(user);
        repository.SendWhenCommitted(user, Records(sent, "first"));
        repository.SendWhenCommitted(user, Records(sent, "second"));
        await repository.SaveChangesAsync();

        Assert.Equal(["second"], sent);
    }

    // ── One send per user for what commits together ─────────────────────────────

    [Fact]
    public async Task Two_repository_saves_of_one_user_in_one_transaction_each_with_an_ask_send_once_the_later_ask_to_the_committed_address()
    {
        // A flow that saves a user, changes the address again and saves, in one transaction: the ask of the later save
        // takes the place of the earlier one, as a second ask before one save does. One link goes, once the user is
        // committed, to the address that was committed. A link for the first address would be void once the second is
        // issued.
        Start();
        var (db, repository, scope) = Scope();
        using var _ = scope;

        var sent = new List<string>();
        var user = AUser("alice");
        user.Email = "alice@first.example";

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

            user.Email = "alice@second.example";
            repository.SendWhenCommitted(user, Link("the later ask"));
            await repository.SaveChangesAsync();

            Assert.Empty(sent);
            await transaction.CommitAsync(Ct);
        }

        Assert.Equal(["the later ask, to alice@second.example"], sent);
        Assert.Equal("alice@second.example", (await db.Users.AsNoTracking().SingleAsync(Ct)).Email);
    }

    [Theory]
    [InlineData("refused before its write")]
    [InlineData("fails after its write")]
    public async Task When_the_later_save_of_the_user_fails_and_the_caller_commits_all_the_same_the_earlier_ask_is_sent(string how)
    {
        // The later save's ask took the earlier one's place in the unit's bag. The failed save puts the bag back as it
        // was when that save began, so the earlier ask, which belongs to the save that is committed, is sent.
        Start(services => services.AddSaveHandler<RefusesTheUser>().AddSaveHandler<FailsAfterTheWrite>());
        var (db, repository, scope) = Scope();
        using var _ = scope;

        var sent = new List<string>();
        var user = AUser("alice");

        await using (var transaction = await db.Database.BeginTransactionAsync(Ct))
        {
            db.Users.Add(user);
            repository.SendWhenCommitted(user, Records(sent, "the earlier ask"));
            await repository.SaveChangesAsync();

            user.Username = how == "refused before its write" ? "refused" : "fails";
            repository.SendWhenCommitted(user, Records(sent, "the later ask"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveChangesAsync());

            // The caller swallows the failure and commits what was saved before it.
            await transaction.CommitAsync(Ct);
        }

        Assert.Equal(["the earlier ask"], sent);
        Assert.Equal("alice", (await db.Users.AsNoTracking().SingleAsync(Ct)).Username);
    }

    [Fact]
    public async Task Every_send_of_a_commit_is_attempted_and_one_that_fails_reaches_the_caller_after_the_commit()
    {
        // An import that saves two users with a link each. The first send that is attempted fails, whichever user it is
        // for: the other user still gets the link, and the failure reaches the caller as it is.
        Start();
        var (db, repository, scope) = Scope();
        using var _ = scope;

        var attempted = new List<string>();
        var sent = new List<string>();

        Func<Task> Link(string name) => () =>
        {
            attempted.Add(name);

            if (attempted.Count == 1)
                throw new InvalidOperationException($"The mail server refused the message to {name}.");

            sent.Add(name);
            return Task.CompletedTask;
        };

        var alice = AUser("alice");
        var bob = AUser("bob");
        db.Users.AddRange(alice, bob);
        repository.SendWhenCommitted(alice, Link("alice"));
        repository.SendWhenCommitted(bob, Link("bob"));

        var failure = await Assert.ThrowsAsync<AfterCommitException>(() => repository.SaveChangesAsync());

        Assert.Equal(2, attempted.Count);
        Assert.Equal([attempted[1]], sent);
        Assert.Equal($"The mail server refused the message to {attempted[0]}.", Assert.IsType<InvalidOperationException>(Assert.Single(failure.Failures)).Message);
        Assert.Equal(2, await db.Users.CountAsync(Ct));
    }

    [Fact]
    public async Task When_several_sends_of_a_commit_fail_they_reach_the_caller_together()
    {
        Start();
        var (db, repository, scope) = Scope();
        using var _ = scope;

        var alice = AUser("alice");
        var bob = AUser("bob");
        db.Users.AddRange(alice, bob);
        repository.SendWhenCommitted(alice, () => throw new InvalidOperationException("The message to alice was refused."));
        repository.SendWhenCommitted(bob, () => throw new InvalidOperationException("The message to bob was refused."));

        var failure = await Assert.ThrowsAsync<AfterCommitException>(() => repository.SaveChangesAsync());

        var together = Assert.IsType<AggregateException>(Assert.Single(failure.Failures));
        Assert.Equal(["The message to alice was refused.", "The message to bob was refused."],
            together.InnerExceptions.Select(exception => exception.Message).Order(StringComparer.Ordinal));
        Assert.Equal(2, await db.Users.CountAsync(Ct));
    }

    // ── A save that does not commit it, or is switched off for it ───────────────

    [Fact]
    public async Task A_save_that_is_refused_before_it_writes_drops_the_send_and_a_later_save_of_the_user_does_not_send_it()
    {
        Start(services => services.AddSaveHandler<RefusesTheUser>());
        var (db, repository, scope) = Scope();
        using var _ = scope;

        var sent = new List<string>();
        var user = AUser("refused");

        db.Users.Add(user);
        repository.SendWhenCommitted(user, Records(sent, "the link"));

        Assert.Equal("The user is refused.", (await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveChangesAsync())).Message);
        Assert.Null(user.verificationSendOfThisSave);

        // The same instance, fixed and saved again: nothing was asked for this save.
        user.Username = "accepted";
        await repository.SaveChangesAsync();

        Assert.Empty(sent);
        Assert.Equal(1, await db.Users.CountAsync(Ct));
    }

    [Fact]
    public async Task A_save_that_fails_after_its_write_drops_the_send_and_a_later_save_of_the_user_does_not_send_it()
    {
        Start(services => services.AddSaveHandler<FailsAfterTheWrite>());
        var (db, repository, scope) = Scope();
        using var _ = scope;

        var sent = new List<string>();
        var user = AUser("fails");

        db.Users.Add(user);
        repository.SendWhenCommitted(user, Records(sent, "the link"));

        Assert.Equal("The save failed after its write.", (await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveChangesAsync())).Message);
        Assert.Equal(0, await db.Users.CountAsync(Ct));

        // Undone, so the context is cleared and the same instance is given to it anew.
        db.ChangeTracker.Clear();
        user.ID = 0;
        user.Username = "saved";
        db.Users.Add(user);
        await repository.SaveChangesAsync();

        Assert.Empty(sent);
        Assert.Equal(1, await db.Users.CountAsync(Ct));
    }

    [Theory]
    [InlineData("SuppressSaveHandler<UserVerificationSend>")]
    [InlineData("SuppressSaveHandlers(AfterCommit)")]
    [InlineData("SuppressAllSaveHandlers")]
    [InlineData("SuppressAfterCommit")]
    public async Task A_save_switched_off_for_the_send_drops_it_and_a_later_save_of_the_user_does_not_send_it(string switchedOff)
    {
        Start();
        var (db, repository, scope) = Scope();
        using var _ = scope;

        var sent = new List<string>();
        var user = AUser("alice");

        db.Users.Add(user);
        repository.SendWhenCommitted(user, Records(sent, "the link"));

        using (switchedOff switch
        {
            "SuppressSaveHandler<UserVerificationSend>" => db.SuppressSaveHandler<UserVerificationSend>(),
            "SuppressSaveHandlers(AfterCommit)" => db.SuppressSaveHandlers(SaveStages.AfterCommit),
            "SuppressAllSaveHandlers" => db.SuppressAllSaveHandlers(),
            _ => db.SuppressAfterCommit(),
        })
        {
            await repository.SaveChangesAsync();
        }

        user.FullName = "Alice";
        await repository.SaveChangesAsync();

        Assert.Empty(sent);
        Assert.Null(user.verificationSendOfThisSave);
    }

    [Fact]
    public async Task Inside_one_transaction_a_save_switched_off_for_the_send_drops_it_also_when_a_later_save_writes_the_user_again()
    {
        Start();
        var (db, repository, scope) = Scope();
        using var _ = scope;

        var sent = new List<string>();
        var user = AUser("alice");

        await using (var transaction = await db.Database.BeginTransactionAsync(Ct))
        {
            db.Users.Add(user);
            repository.SendWhenCommitted(user, Records(sent, "the link"));

            using (db.SuppressAfterCommit())
                await repository.SaveChangesAsync();

            // The user's row of this later save reaches the after-commit stage at the commit. The send of the save
            // that was switched off does not come with it.
            user.FullName = "Alice";
            await db.SaveChangesAsync(Ct);

            await transaction.CommitAsync(Ct);
        }

        Assert.Empty(sent);
    }

    // ── The same user loaded again ──────────────────────────────────────────────

    [Fact]
    public async Task A_user_saved_then_loaded_again_as_another_instance_in_the_same_transaction_is_still_sent_to_at_the_commit()
    {
        Start();
        var (db, repository, scope) = Scope();
        using var _ = scope;

        var sent = new List<string>();
        var user = AUser("alice");

        await using (var transaction = await db.Database.BeginTransactionAsync(Ct))
        {
            db.Users.Add(user);
            repository.SendWhenCommitted(user, Records(sent, "the link"));
            await repository.SaveChangesAsync();

            // An import that clears the tracker between saves: the same record, as another instance.
            db.ChangeTracker.Clear();

            var again = await db.Users.SingleAsync(x => x.ID == user.ID, Ct);
            Assert.NotSame(user, again);

            again.FullName = "Alice";
            await db.SaveChangesAsync(Ct);

            Assert.Empty(sent);
            await transaction.CommitAsync(Ct);
        }

        Assert.Equal(["the link"], sent);
    }

    // ── What it is ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_handler_is_public_takes_nothing_keeps_nothing_for_the_process_and_runs_on_a_context_without_services()
    {
        var type = typeof(UserVerificationSend);

        // Public, so that code can name it in SuppressSaveHandler, and sealed.
        Assert.True(type is { IsPublic: true, IsSealed: true });
        Assert.True(typeof(IBeforeSaveHandler<User>).IsAssignableFrom(type));
        Assert.True(typeof(IAfterCommitHandler<User>).IsAssignableFrom(type));

        // It takes nothing, so a context built without services creates it, and it keeps no state of its own.
        Assert.Equal([0], type.GetConstructors().Select(constructor => constructor.GetParameters().Length));
        Assert.Empty(type.GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));

        // A context built with new, as a tool builds one: what a save hands to a user is sent at that save's commit.
        connection.Open();
        await using var db = new IdentityDb(new DbContextOptionsBuilder<IdentityDb>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(Ct);

        var sent = new List<string>();
        var user = AUser("alice");
        db.Users.Add(user);
        user.verificationSendOfThisSave = Records(sent, "the link");

        await db.SaveChangesAsync(Ct);

        Assert.Equal(["the link"], sent);
        Assert.Null(user.verificationSendOfThisSave);
    }
}
