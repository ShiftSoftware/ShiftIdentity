using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ShiftSoftware.ShiftIdentity.Data.Authentication;

/// <summary>
/// Uses the authoritative identity context. Each instance is scoped to one authentication request;
/// no configured connection, replica, fallback database or process-local lock is used here.
/// </summary>
public sealed class SqlIdentitySecurityStore(ShiftIdentityDbContext db) : IIdentitySecurityStore
{
    public async Task<IdentityProofSnapshot?> ReadProofAsync(string username, AuthenticationClient client, CancellationToken ct)
    {
        try
        {
            var ids = await db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.Username == username).Select(x => x.ID).Take(2).ToListAsync(ct);
            if (ids.Count != 1) return null;
            return await AdmitAsync(ids[0], null, client, unit =>
                Task.FromResult(new IdentityProofSnapshot(unit.User, unit.Security, unit.Policy)), ct);
        }
        catch (SqlException e) { throw new IdentitySecurityUnavailableException("Identity store unavailable.", e); }
    }

    public async Task<AuthenticationOperation?> ReadOperationAsync(Guid id, CancellationToken ct)
    {
        try
        {
            return await db.Set<AuthenticationOperation>().AsNoTracking().SingleOrDefaultAsync(x => x.ID == id, ct);
        }
        catch (SqlException e) { throw new IdentitySecurityUnavailableException("Identity store unavailable.", e); }
    }

    public Task<T> AdmitAsync<T>(long userID, Guid? operationID, AuthenticationClient client,
        Func<IdentitySecurityTransaction, Task<T>> transition, CancellationToken ct) =>
        AdmitUsersAsync([userID], userID, operationID, client, units => transition(units[userID]), ct);

    public Task<T> AdmitAdminAsync<T>(long actorID, long userID, AuthenticationClient client,
        Func<IdentitySecurityTransaction, IdentitySecurityTransaction, Task<T>> transition, CancellationToken ct) =>
        AdmitUsersAsync([actorID, userID], userID, null, client, units => transition(units[actorID], units[userID]), ct);

    private async Task<T> AdmitUsersAsync<T>(long[] userIDs, long targetID, Guid? operationID, AuthenticationClient client,
        Func<Dictionary<long, IdentitySecurityTransaction>, Task<T>> transition, CancellationToken ct, bool requireClient = true)
    {
        // No execution-strategy replay: an uncertain commit must not repeat a consumed operation.
        // A caller can restart with fresh proof after an unavailable response.
        db.ChangeTracker.Clear();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var policy = await db.Set<AuthenticationPolicyState>()
                .FromSqlRaw("SELECT * FROM [ShiftIdentity].[AuthenticationPolicyStates] WITH (HOLDLOCK) WHERE [ID] = 1")
                .SingleOrDefaultAsync(ct)
                ?? throw new IdentitySecurityUnavailableException("Identity policy state is missing.");
            var app = await db.Apps.FromSqlInterpolated(
                $"SELECT * FROM [ShiftIdentity].[Apps] WITH (HOLDLOCK) WHERE [AppId] = {client.ID}")
                .IgnoreQueryFilters().SingleOrDefaultAsync(ct);
            if (requireClient && (app is null || app.IsDeleted))
                throw new IdentitySecurityConflictException("Client unavailable.");
            var units = new Dictionary<long, IdentitySecurityTransaction>();
            // Actor and target locks have a stable order, including requests that target each other.
            foreach (var userID in userIDs.Distinct().Order())
            {
                var security = await db.Set<UserSecurityState>().FromSqlInterpolated(
                    $"SELECT * FROM [ShiftIdentity].[UserSecurityStates] WITH (UPDLOCK, HOLDLOCK) WHERE [UserID] = {userID}")
                    .SingleOrDefaultAsync(ct)
                    ?? throw new IdentitySecurityUnavailableException("User security state is missing.");
                var user = await db.Users.IgnoreQueryFilters()
                    .Include(x => x.Company).Include(x => x.CompanyBranch)
                    .Include(x => x.TeamUsers).Include(x => x.AccessTrees).ThenInclude(x => x.AccessTree)
                    .SingleOrDefaultAsync(x => x.ID == userID, ct)
                    ?? throw new IdentitySecurityUnavailableException("User is missing.");
                var operation = userID != targetID || operationID is null ? null
                    : await db.Set<AuthenticationOperation>().SingleOrDefaultAsync(x => x.ID == operationID, ct);
                var recoveryFamily = security.MfaRecoveryOperationID is { } root
                    ? await db.Set<AuthenticationOperation>().Where(x => x.UserID == userID && (x.ID == root || x.ParentID == root)).ToListAsync(ct)
                    : [];
                var unit = new IdentitySecurityTransaction(user, security, policy, operation,
                    value => db.Set<AuthenticationOperation>().Add(value),
                    value => db.Set<AuthenticationAuditEvent>().Add(value), recoveryFamily);
                units.Add(userID, unit);
            }
            var result = await transition(units);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateConcurrencyException e) { throw new IdentitySecurityConflictException("Concurrent security change.", e); }
        catch (DbUpdateException e) { throw new IdentitySecurityUnavailableException("Security transaction failed.", e); }
        catch (SqlException e) { throw new IdentitySecurityUnavailableException("Security transaction unavailable.", e); }
        catch (IOException e) { throw new IdentitySecurityUnavailableException("Security commit outcome unavailable.", e); }
        finally { db.ChangeTracker.Clear(); }
    }

    /// <summary>Clears expired protected payloads under the same user lock; removes old terminal tombstones in bounded batches.</summary>
    public async Task<int> CleanupAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var proofCutoff = now.AddMinutes(-5);
        var candidates = await db.Set<AuthenticationOperation>().AsNoTracking().Where(x =>
            (x.State == AuthenticationOperationState.AwaitingMfa || x.State == AuthenticationOperationState.AwaitingPassword ||
             x.State == AuthenticationOperationState.AwaitingNewPassword || x.State == AuthenticationOperationState.AwaitingNewFactor ||
             x.State == AuthenticationOperationState.AwaitingRecoveryProof) &&
            (x.ExpiresAt <= now || x.PasswordProvenAt <= proofCutoff || x.MfaProvenAt <= proofCutoff))
            .OrderBy(x => x.ExpiresAt).Take(100).ToListAsync(ct);
        var count = 0;
        foreach (var candidate in candidates)
        {
            count += await AdmitUsersAsync([candidate.UserID], candidate.UserID, candidate.ID,
                new(candidate.ClientID, candidate.Audience, candidate.External), units =>
                {
                    var unit = units[candidate.UserID]; var op = unit.Operation;
                    if (op is null || op.State is AuthenticationOperationState.Completed or AuthenticationOperationState.Cancelled or
                        AuthenticationOperationState.Locked or AuthenticationOperationState.Superseded ||
                        !(op.ExpiresAt <= now || op.PasswordProvenAt <= proofCutoff || op.MfaProvenAt <= proofCutoff)) return Task.FromResult(0);
                    op.State = AuthenticationOperationState.Cancelled; op.CompletedAt = now;
                    op.HandleDigest = []; op.CodeChallenge = ""; op.PendingPasswordHash = null; op.PendingPasswordSalt = null;
                    op.ProtectedPendingTotpSecret = null; op.RecoveryCodeDigest = null; op.OutstandingRecoveryUserID = null;
                    unit.Audit("OperationExpired", now, op.ID);
                    return Task.FromResult(1);
                }, ct, requireClient: false);
        }
        var retentionCutoff = now.AddHours(-24);
        var tombstones = await db.Set<AuthenticationOperation>().Where(x => x.CompletedAt < retentionCutoff &&
            (x.State == AuthenticationOperationState.Completed || x.State == AuthenticationOperationState.Cancelled ||
             x.State == AuthenticationOperationState.Locked || x.State == AuthenticationOperationState.Superseded) &&
            !db.Set<UserSecurityState>().Any(s => s.MfaRecoveryOperationID == x.ID))
            .OrderBy(x => x.CompletedAt).Select(x => x.ID).Take(100).ToListAsync(ct);
        if (tombstones.Count > 0) await db.Set<AuthenticationOperation>().Where(x => tombstones.Contains(x.ID)).ExecuteDeleteAsync(ct);
        return count;
    }
}
