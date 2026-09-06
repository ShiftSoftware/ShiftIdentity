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

    public async Task<T> AdmitAsync<T>(long userID, Guid? operationID, AuthenticationClient client,
        Func<IdentitySecurityTransaction, Task<T>> transition, CancellationToken ct)
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
            if (app is null || app.IsDeleted)
                throw new IdentitySecurityConflictException("Client unavailable.");
            var security = await db.Set<UserSecurityState>().FromSqlInterpolated(
                $"SELECT * FROM [ShiftIdentity].[UserSecurityStates] WITH (UPDLOCK, HOLDLOCK) WHERE [UserID] = {userID}")
                .SingleOrDefaultAsync(ct)
                ?? throw new IdentitySecurityUnavailableException("User security state is missing.");
            var user = await db.Users.IgnoreQueryFilters()
                .Include(x => x.Company).Include(x => x.CompanyBranch)
                .Include(x => x.TeamUsers).Include(x => x.AccessTrees).ThenInclude(x => x.AccessTree)
                .SingleOrDefaultAsync(x => x.ID == userID, ct)
                ?? throw new IdentitySecurityUnavailableException("User is missing.");
            var operation = operationID is null ? null
                : await db.Set<AuthenticationOperation>().SingleOrDefaultAsync(x => x.ID == operationID, ct);
            var unit = new IdentitySecurityTransaction(user, security, policy, operation,
                value => db.Set<AuthenticationOperation>().Add(value),
                value => db.Set<AuthenticationAuditEvent>().Add(value));
            var result = await transition(unit);
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
}
