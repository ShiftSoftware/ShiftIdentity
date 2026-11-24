using System.Runtime.ExceptionServices;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.Data;

/// <summary>
/// Sends what the User form asked to send to a user, the verification link for a new address, once the save that
/// writes that user is committed. A save handler that the identity model declares, so every host that saves users has
/// it. It is public so that code can switch it off by name: <c>SuppressSaveHandler&lt;UserVerificationSend&gt;()</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>How a send travels.</b> The User upsert hook decides whether to send: only it sees the administrator's choice on
/// the form. It runs before the save, where nothing may be sent yet, because a created user has no ID and the save can
/// still fail. So the hook asks the user repository, and the repository's next save hands the send to the user for as
/// long as that save runs. This handler's before-save stage takes it from there into the save's unit, and its
/// after-commit stage sends it once the user is committed: by the save itself, by the repository's own transaction, or
/// by a transaction the caller began around the save. Nothing about a send is kept for the process or for a later save.
/// </para>
/// <para>
/// <b>One send per user for what commits together.</b> A user saved twice in one transaction, with an ask each time,
/// gets one link at the commit: the later ask takes the place of the earlier one, as a second ask before one save does.
/// If the later save fails and the caller still commits, the earlier ask is sent: the failed save puts the unit's bag
/// back as it was when that save began.
/// </para>
/// <para>
/// <b>When nothing is sent.</b> A save that is refused, fails or is rolled back sends nothing, and the send is dropped
/// with it: the unit and its bag go back or go away, and the user no longer carries the send once the repository's save
/// has ended. So does a save that is switched off for this handler's after-commit stage:
/// <c>SuppressSaveHandler&lt;UserVerificationSend&gt;()</c>, <c>SuppressSaveHandlers(SaveStages.AfterCommit)</c>,
/// <c>SuppressAllSaveHandlers()</c> and <c>SuppressAfterCommit()</c>. And so does a save that is switched off for the
/// before-save stage alone, <c>SuppressSaveHandlers(SaveStages.BeforeSave)</c>: the send travels through that stage,
/// so the after-commit stage, which that switch leaves on, never gets it. A later save of the same user never sends it.
/// </para>
/// <para>
/// <b>A user loaded again.</b> A transaction that saves the user, loads it again as another instance and saves it
/// again still sends at its commit: the send is matched to the committed user by its key.
/// </para>
/// <para>
/// <b>A send that fails</b> undoes nothing, and does not keep the other users of the commit from their links: every
/// send is attempted. The failures reach the caller in the <c>AfterCommitException</c> of the call that committed, the
/// send's own exception when one failed and an <see cref="AggregateException"/> when several did. The CRUD endpoints
/// answer that as "saved", with the failure as the message.
/// </para>
/// </remarks>
public sealed class UserVerificationSend : IBeforeSaveHandler<User>, IAfterCommitHandler<User>
{
    /// <summary>
    /// Takes each send that the repository's save handed to a user it is about to write into the save's unit, in the
    /// place of one that an earlier save of the unit took for the same user, unless this save will not reach the
    /// after-commit stage of this handler.
    /// </summary>
    public ValueTask BeforeSaveAsync(PendingRows<User> rows, CancellationToken cancellationToken)
    {
        // Under a switch that keeps this save's rows from the after-commit stage, the send is dropped here: it would
        // otherwise wait in the bag for a later save of the same transaction to send it.
        var sends = !rows.IsSwitchedOff(SaveStages.AfterCommit);

        foreach (var row in rows)
        {
            var send = row.Entity.verificationSendOfThisSave;

            if (send is null)
                continue;

            row.Entity.verificationSendOfThisSave = null;

            if (!sends || row.Change == RowChange.Deleted)
                continue;

            // One send per user for what commits together: the later ask counts.
            ForgetEarlierSends(rows.Items, row.Entity);

            // A new key for each send, so the bag can put it back exactly when the save is undone.
            rows.Items[new Waiting(row.Entity)] = send;
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Sends what waits for each committed user, once. Every send is attempted, whichever of them fails.</summary>
    public async ValueTask AfterCommitAsync(SavedRows<User> rows, CancellationToken cancellationToken)
    {
        List<Waiting>? waiting = null;

        foreach (var key in rows.Items.Keys)
            if (key is Waiting send)
                (waiting ??= []).Add(send);

        if (waiting is null)
            return;

        List<Exception>? failures = null;

        foreach (var row in rows)
        {
            if (row.Change == RowChange.Deleted)
                continue;

            foreach (var send in waiting)
            {
                if (!send.IsFor(row.Entity) || !rows.Items.TryGetValue(send, out var work) || work is not Func<Task> run)
                    continue;

                rows.Items.Remove(send);

                try
                {
                    await run();
                }
                catch (Exception failure)
                {
                    (failures ??= []).Add(failure);
                }
            }
        }

        if (failures is [var only])
            ExceptionDispatchInfo.Throw(only);

        if (failures is not null)
            throw new AggregateException($"The users were committed, but {failures.Count} of the sends asked for them failed.", failures);
    }

    // Removes from the bag what an earlier save of the unit left waiting for the same user.
    private static void ForgetEarlierSends(IDictionary<object, object?> items, User user)
    {
        List<object>? earlier = null;

        foreach (var key in items.Keys)
            if (key is Waiting waiting && waiting.IsFor(user))
                (earlier ??= []).Add(key);

        if (earlier is null)
            return;

        foreach (var key in earlier)
            items.Remove(key);
    }

    // What waits in the unit's bag: the user the send was asked for, as the instance the save wrote.
    private sealed class Waiting(User user)
    {
        // The same instance, or the same record loaded again as another instance after the save gave it its key.
        public bool IsFor(User committed) => ReferenceEquals(user, committed) || (user.ID != 0 && user.ID == committed.ID);
    }
}
