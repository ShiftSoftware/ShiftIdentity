using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ShiftIdentity.Tests.Infrastructure;

/// <summary>
/// Reads the log messages of replication on save. "Starting" is logged while the save runs; "Succeeded" or "Failed"
/// when the background task has finished, the bookkeeping write included. A copy of the class of the same name in
/// ShiftEntity.Tests/Replication/ReplicationPinHost.cs; keep the two the same.
/// </summary>
public sealed class ReplicationSyncLog : ILoggerProvider, ILogger
{
    private readonly ConcurrentQueue<string> started = new();
    private readonly ConcurrentQueue<string> failed = new();
    private readonly ConcurrentQueue<string> warnings = new();
    private readonly SemaphoreSlim finished = new(0);
    private int waitedFor;

    /// <summary>Each started sync, as "type id", in order.</summary>
    public IReadOnlyList<string> Started => started.ToList();

    /// <summary>The message of each failed sync.</summary>
    public IReadOnlyList<string> Failed => failed.ToList();

    /// <summary>Every warning, for example a catch-up run's report of failed rows.</summary>
    public IReadOnlyList<string> Warnings => warnings.ToList();

    /// <summary>
    /// Waits until every sync started so far has finished. It waits for the sync's message, not for a set time. A sync
    /// that failed has finished too, so a test that expects every sync to succeed says so with
    /// <see cref="AssertNoneFailed"/>.
    /// </summary>
    public async Task AllFinishedAsync()
    {
        while (waitedFor < started.Count)
        {
            Assert.True(await finished.WaitAsync(TimeSpan.FromSeconds(30)), "A replication sync did not finish.");
            waitedFor++;
        }
    }

    /// <summary>
    /// Fails the test with the whole message of every sync that has failed so far, its exception included. Without it a
    /// failed sync shows only as a document or a record that is missing, and <c>Assert.Empty(Failed)</c> shows only the
    /// first characters of the message.
    /// </summary>
    public void AssertNoneFailed()
    {
        if (!failed.IsEmpty)
            Assert.Fail($"{failed.Count} of the replication syncs failed:{Environment.NewLine}{string.Join(Environment.NewLine, failed)}");
    }

    public ILogger CreateLogger(string categoryName) => this;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning)
            warnings.Enqueue(formatter(state, exception));

        if (state is not IReadOnlyList<KeyValuePair<string, object?>> values ||
            values.LastOrDefault().Value is not string format)
            return;

        if (format.StartsWith("CosmosDB Syncing is starting"))
        {
            var type = values.First(x => x.Key == "entityType").Value as Type;
            started.Enqueue($"{type?.Name} {values.First(x => x.Key == "entityID").Value}");
        }
        else if (format.StartsWith("CosmosDB Syncing Succeeded"))
            finished.Release();
        else if (format.StartsWith("CosmosDB Syncing Failed"))
        {
            failed.Enqueue(formatter(state, exception));
            finished.Release();
        }
    }

    public void Dispose() { }
}
