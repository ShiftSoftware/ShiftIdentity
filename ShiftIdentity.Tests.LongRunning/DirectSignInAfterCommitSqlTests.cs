using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// The direct issuer that a host without the authority keeps (<see cref="AuthService"/>) saves sign-in bookkeeping
/// through the user repository: the attempts of a wrong password, the attempts and the last-seen time of a sign-in, and
/// the last-seen time of a refresh. When a step that runs after the commit of such a save fails, such as an after-commit
/// handler the host registered, the bookkeeping is committed: the call goes on as saved, and each failure is logged.
/// The class has a database of its own, because it adds a user, and the classes that share the "Identity SQL" database
/// expect the fixture's one user.
/// </summary>
public sealed class DirectSignInAfterCommitSqlTests(SqlIdentityFixture fixture) : IClassFixture<SqlIdentityFixture>
{
    /// <summary>Makes the host's after-commit handler fail, while it is armed.</summary>
    public sealed class FailAfterTheCommit
    {
        public bool Armed { get; set; }
    }

    /// <summary>
    /// A host's own after-commit handler on users and their log rows, such as one that tells a directory who signed in
    /// and when. It fails while armed. A wrong password writes the user, a sign-in the user and its log row, and a
    /// refresh only the log row.
    /// </summary>
    public sealed class TellsTheDirectory(FailAfterTheCommit fail) : IAfterCommitHandler<User>, IAfterCommitHandler<UserLog>
    {
        public ValueTask AfterCommitAsync(SavedRows<User> rows, CancellationToken cancellationToken) => Tell();

        public ValueTask AfterCommitAsync(SavedRows<UserLog> rows, CancellationToken cancellationToken) => Tell();

        private ValueTask Tell() => fail.Armed ? throw new InvalidOperationException("The directory could not be told.") : ValueTask.CompletedTask;
    }

    /// <summary>Keeps what the host logs as an error.</summary>
    public sealed class RecordedErrors : ILoggerProvider
    {
        private readonly List<(string Category, Exception? Exception)> entries = [];

        public List<(string Category, Exception? Exception)> Entries
        {
            get
            {
                lock (entries)
                    return [.. entries];
            }
        }

        public ILogger CreateLogger(string categoryName) => new Recorder(this, categoryName);

        public void Dispose() { }

        private sealed class Recorder(RecordedErrors errors, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                    return;

                lock (errors.entries)
                    errors.entries.Add((category, exception));
            }
        }
    }

    private static async Task<LoginResultModel> SignInAsync(LegacyIdentityHttpHost<IdentityTestDbContext> host, string username, string password)
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AuthService>().LoginAsync(new LoginDTO { Username = username, Password = password });
    }

    private async Task<(int Attempts, DateTimeOffset? LastSeen)> BookkeepingAsync(long id)
    {
        await using var db = fixture.CreateContext();
        var user = await db.Users.AsNoTracking().Include(x => x.UserLog).SingleAsync(x => x.ID == id, TestContext.Current.CancellationToken);
        return (user.LoginAttempts, user.UserLog?.LastSeen);
    }

    [Fact]
    public async Task A_wrong_password_a_sign_in_and_a_refresh_go_on_as_saved_when_a_step_after_the_commit_fails_and_each_failure_is_logged()
    {
        var fail = new FailAfterTheCommit();
        var errors = new RecordedErrors();
        var username = "after-commit-" + Guid.NewGuid().ToString("N");
        var id = await fixture.CreateSyntheticUserAsync(username);

        using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, services => services
            .AddSingleton(fail)
            .AddSaveHandler<TellsTheDirectory>()
            .AddSingleton<ILoggerProvider>(errors));

        fail.Armed = true;

        // A wrong password gets its usual answer, and the attempt is counted.
        var wrong = await SignInAsync(host, username, "not the password");
        Assert.Equal(LoginResultEnum.PasswordIncorrect, wrong.Result);
        Assert.Null(wrong.Token);
        Assert.Equal(1, (await BookkeepingAsync(id)).Attempts);

        // The password: the token is issued, the attempts are reset and the time is kept.
        var signedIn = await SignInAsync(host, username, fixture.Password);
        Assert.Equal(LoginResultEnum.Success, signedIn.Result);
        Assert.False(string.IsNullOrEmpty(signedIn.Token?.Token));

        var afterSignIn = await BookkeepingAsync(id);
        Assert.Equal(0, afterSignIn.Attempts);
        Assert.NotNull(afterSignIn.LastSeen);

        // A refresh returns a token, and keeps the time again.
        await using (var setup = fixture.CreateContext())
            await setup.Set<UserLog>().Where(x => x.UserID == id).ExecuteUpdateAsync(x => x.SetProperty(log => log.LastSeen, DateTimeOffset.UnixEpoch),
                TestContext.Current.CancellationToken);

        TokenDTO? refreshed;
        using (var scope = host.Services.CreateScope())
            refreshed = await scope.ServiceProvider.GetRequiredService<AuthService>().RefreshAsync(signedIn.Token!.RefreshToken);

        Assert.False(string.IsNullOrEmpty(refreshed?.Token));
        Assert.True((await BookkeepingAsync(id)).LastSeen > DateTimeOffset.UnixEpoch);

        // Every failure is logged by the issuer: one for the wrong password's save (the user), two for the sign-in's
        // (the user and its log row), and one for the refresh's (the log row).
        var logged = errors.Entries.Where(entry => entry.Category == typeof(AuthService).FullName).ToList();
        Assert.Equal(4, logged.Count);
        Assert.All(logged, entry => Assert.Equal("The directory could not be told.", entry.Exception?.Message));
    }
}
