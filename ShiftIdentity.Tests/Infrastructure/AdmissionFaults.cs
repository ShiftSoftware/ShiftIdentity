using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShiftSoftware.ShiftIdentity.Data.Authentication;

namespace ShiftIdentity.Tests.Infrastructure;

public sealed class AdmissionGate(string checkpoint) : IDisposable
{
    private readonly ManualResetEventSlim release = new();
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int claimed;
    public Task Entered => entered.Task;
    public void Observe(string point)
    {
        if (point != checkpoint || Interlocked.Exchange(ref claimed, 1) != 0) return;
        entered.TrySetResult();
        if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Admission gate was not released.");
    }
    public void Release() => release.Set();
    public void Dispose() { release.Set(); release.Dispose(); }
}

public sealed class SqlCommandSignal(string contains) : DbCommandInterceptor
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Entered => entered.Task;
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains(contains, StringComparison.Ordinal)) entered.TrySetResult();
        return ValueTask.FromResult(result);
    }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains(contains, StringComparison.Ordinal)) entered.TrySetResult();
        return ValueTask.FromResult(result);
    }
}

public sealed class CompletionSaveFault(bool afterSave) : SaveChangesInterceptor
{
    private int raised;
    private void Fail(DbContext? context, bool atAfterSave)
    {
        if (afterSave != atAfterSave || context is null ||
            !context.ChangeTracker.Entries<AuthenticationOperation>().Any(x => x.Entity.State == AuthenticationOperationState.Completed)) return;
        if (Interlocked.Exchange(ref raised, 1) == 0) throw new IOException("Synthetic save transport failure.");
    }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Fail(eventData.Context, false);
        return ValueTask.FromResult(result);
    }
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        Fail(eventData.Context, true);
        return ValueTask.FromResult(result);
    }
}

public sealed class LostCommitResponse : DbTransactionInterceptor
{
    private int raised;
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref raised, 1) == 0) throw new IOException("Synthetic lost commit response.");
        return Task.CompletedTask;
    }
}
