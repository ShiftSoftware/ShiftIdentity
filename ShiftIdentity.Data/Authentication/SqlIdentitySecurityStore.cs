using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Authentication;

/// <summary>
/// Uses the authoritative identity context. Each instance is scoped to one authentication request;
/// no configured connection, replica, fallback database or process-local lock is used here.
/// </summary>
public sealed partial class SqlIdentitySecurityStore(ShiftIdentityDbContext db) : IIdentitySecurityStore
{
    /// <summary>True when this store works on the given context instance. The legacy writers must share one context.</summary>
    public bool Shares(DbContext context) => ReferenceEquals(db, context);

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

    public Task<T> AdmitLegacyRefreshAsync<T>(long userID, byte[] credentialDigest, AuthenticationClient client,
        Func<IdentitySecurityTransaction, Task<T>> transition, CancellationToken ct) =>
        AdmitLegacyCredentialAsync(userID, AuthenticationOperationPurpose.LegacyRefreshExchange, credentialDigest, client, transition, ct);

    public Task<T> AdmitLegacyCredentialAsync<T>(long userID, AuthenticationOperationPurpose purpose, byte[] credentialDigest,
        AuthenticationClient client, Func<IdentitySecurityTransaction, Task<T>> transition, CancellationToken ct)
    {
        if (purpose is not (AuthenticationOperationPurpose.LegacyRefreshExchange or AuthenticationOperationPurpose.LegacyMfaExchange))
            throw new ArgumentException("Only a pre-cutover credential purpose is keyed by its digest.", nameof(purpose));
        if (credentialDigest is not { Length: 32 })
            throw new ArgumentException("A legacy credential digest must be 32 bytes.", nameof(credentialDigest));
        return AdmitUsersAsync([userID], userID, null, client, units => transition(units[userID]), ct,
            legacyCredential: (purpose, credentialDigest));
    }

    public Task<T> AdmitAdminAsync<T>(long actorID, long userID, AuthenticationClient client,
        Func<IdentitySecurityTransaction, IdentitySecurityTransaction, Task<T>> transition, CancellationToken ct) =>
        AdmitUsersAsync([actorID, userID], userID, null, client, units => transition(units[actorID], units[userID]), ct);

    public Task<T> AdmitAppAsync<T>(long userID, AuthenticationClient source, AuthenticationClient destination,
        Func<IdentitySecurityTransaction, Task<T>> transition, CancellationToken ct) =>
        AdmitUsersAsync([userID], userID, null, destination, units => transition(units[userID]), ct, sourceClient: source);

    public async Task<IReadOnlyDictionary<long, IdentitySecurityTransaction>> AdmitWithinAsync(IEnumerable<long> userIDs,
        AuthenticationClient client, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Admission inside a caller's transaction requires that transaction to be open.");
        try
        {
            // The caller's tracked rows stay tracked: the same instances receive the admitted changes and one flush
            // writes them with the caller's own edits. Nothing is committed here.
            return await LoadUnitsAsync(userIDs.ToArray(), null, null, client, requireClient: true, reuseTracked: true, ct);
        }
        catch (SqlException e) { throw new IdentitySecurityUnavailableException("Security transaction unavailable.", e); }
    }

    private async Task<T> AdmitUsersAsync<T>(long[] userIDs, long targetID, Guid? operationID, AuthenticationClient client,
        Func<Dictionary<long, IdentitySecurityTransaction>, Task<T>> transition, CancellationToken ct, bool requireClient = true,
        AuthenticationClient? sourceClient = null, (AuthenticationOperationPurpose Purpose, byte[] Digest)? legacyCredential = null)
    {
        // No execution-strategy replay: an uncertain commit must not repeat a consumed operation.
        // A caller can restart with fresh proof after an unavailable response.
        db.ChangeTracker.Clear();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var units = await LoadUnitsAsync(userIDs, targetID, operationID, client, requireClient, reuseTracked: false, ct,
                sourceClient, legacyCredential);
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

    // The one lock order for every security writer: policy, Apps in ordinal AppId order, then users in ascending ID order, and for each user
    // its security state before its operation, recovery family and link rows.
    private async Task<Dictionary<long, IdentitySecurityTransaction>> LoadUnitsAsync(long[] userIDs, long? targetID, Guid? operationID,
        AuthenticationClient client, bool requireClient, bool reuseTracked, CancellationToken ct,
        AuthenticationClient? sourceClient = null, (AuthenticationOperationPurpose Purpose, byte[] Digest)? legacyCredential = null)
    {
        var policy = await Hinted<AuthenticationPolicyState>("HOLDLOCK", nameof(AuthenticationPolicyState.ID), 1)
            .SingleOrDefaultAsync(ct)
            ?? throw new IdentitySecurityUnavailableException("Identity policy state is missing.");
        App? app = null;
        var clientIDs = sourceClient is null ? new[] { client.ID } : new[] { client.ID, sourceClient.ID };
        foreach (var clientID in clientIDs.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var matches = await Hinted<App>("HOLDLOCK", nameof(App.AppId), clientID)
                .IgnoreQueryFilters().Where(x => !x.IsDeleted).Take(2).ToListAsync(ct);
            if (requireClient && matches.Count != 1)
                throw new IdentitySecurityConflictException("Client unavailable.");
            if (clientID == client.ID) app = matches.Count == 1 ? matches[0] : null;
        }
        var units = new Dictionary<long, IdentitySecurityTransaction>();
        // Actor and target locks have a stable order, including requests that target each other.
        foreach (var userID in userIDs.Distinct().Order())
        {
            // A missing row refuses; it is never defaulted here. The rows of users that existed before the authority
            // are created by the authority's startup (UserSecurityExpansion), and every admitted creation records one.
            var security = await Hinted<UserSecurityState>("UPDLOCK, HOLDLOCK", nameof(UserSecurityState.UserID), userID)
                .SingleOrDefaultAsync(ct)
                ?? throw new IdentitySecurityUnavailableException("User security state is missing.");
            var user = reuseTracked ? db.ChangeTracker.Entries<User>().FirstOrDefault(x => x.Entity.ID == userID)?.Entity : null;
            user ??= await db.Users.IgnoreQueryFilters()
                .Include(x => x.Company).Include(x => x.CompanyBranch).Include(x => x.UserLog)
                .Include(x => x.TeamUsers).Include(x => x.AccessTrees).ThenInclude(x => x.AccessTree)
                .SingleOrDefaultAsync(x => x.ID == userID, ct)
                ?? throw new IdentitySecurityUnavailableException("User is missing.");
            var operation = userID != targetID ? null : operationID is not null
                ? await db.Set<AuthenticationOperation>().SingleOrDefaultAsync(x => x.ID == operationID, ct)
                : legacyCredential is not { } credential ? null
                : await db.Set<AuthenticationOperation>().SingleOrDefaultAsync(x =>
                    x.Purpose == credential.Purpose && x.HandleDigest == credential.Digest, ct);
            var recoveryFamily = security.MfaRecoveryOperationID is { } root
                ? await db.Set<AuthenticationOperation>().Where(x => x.UserID == userID && (x.ID == root || x.ParentID == root)).ToListAsync(ct)
                : [];
            var links = await db.Set<AuthenticationOperation>().Where(x => x.UserID == userID && x.OutstandingLinkSlot != null).ToListAsync(ct);
            var unit = new IdentitySecurityTransaction(user, security, policy, operation,
                value => db.Set<AuthenticationOperation>().Add(value),
                value => db.Set<AuthenticationAuditEvent>().Add(value), recoveryFamily, links, app);
            units.Add(userID, unit);
        }
        return units;
    }

    /// <summary>
    /// The single place that builds a hinted single-row read. The schema, table, column and index names come from
    /// the EF model, so a mapping change cannot leave a literal behind; the value is always a parameter. The columns
    /// are named explicitly rather than read with <c>SELECT *</c>: a host that maps the identity entities as temporal
    /// tables (the template's <c>UseTemporal</c>) declares their period columns hidden, which <c>SELECT *</c> omits and
    /// the composed query then cannot find.
    /// </summary>
    private IQueryable<T> Hinted<T>(string hints, string property, object value, string? indexOn = null) where T : class
    {
        var entity = db.Model.FindEntityType(typeof(T)) ?? throw new InvalidOperationException($"{typeof(T).Name} is not part of this model.");
        var tableName = entity.GetTableName() ?? throw new InvalidOperationException($"{typeof(T).Name} has no table.");
        var schema = entity.GetSchema() ?? db.Model.GetDefaultSchema() ?? "dbo";
        var table = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
        var column = entity.FindProperty(property)?.GetColumnName(table)
            ?? throw new InvalidOperationException($"{typeof(T).Name}.{property} has no column.");
        var columns = string.Join(", ", entity.GetProperties().Select(x => x.GetColumnName(table))
            .Where(x => x is not null).Distinct(StringComparer.Ordinal).Select(x => $"[{x}]"));
        var hint = hints;
        if (indexOn is not null)
        {
            var index = entity.GetIndexes().SingleOrDefault(x => x.Properties.Count == 1 && x.Properties[0].Name == indexOn)?.GetDatabaseName(table)
                ?? throw new InvalidOperationException($"{typeof(T).Name}.{indexOn} has no single-column index.");
            hint = $"{hints}, INDEX({index})";
        }
        return db.Set<T>().FromSqlRaw($"SELECT {columns} FROM [{schema}].[{tableName}] WITH ({hint}) WHERE [{column}] = {{0}}", value);
    }

    /// <summary>Clears expired protected payloads under the same user lock; removes old terminal tombstones in bounded batches.</summary>
    public async Task<int> CleanupAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var proofCutoff = now.AddMinutes(-5);
        var candidates = await db.Set<AuthenticationOperation>().AsNoTracking().Where(x =>
            (x.State == AuthenticationOperationState.AwaitingMfa || x.State == AuthenticationOperationState.AwaitingPassword ||
             x.State == AuthenticationOperationState.AwaitingNewPassword || x.State == AuthenticationOperationState.AwaitingNewFactor ||
             x.State == AuthenticationOperationState.AwaitingRecoveryProof || x.State == AuthenticationOperationState.AwaitingExplicitSubmit ||
             x.State == AuthenticationOperationState.AwaitingAppExchange) &&
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
                    // A legacy MFA row keeps its credential digest: the cancelled row stays that credential's tombstone until its expiry.
                    if (op.Purpose != AuthenticationOperationPurpose.LegacyMfaExchange) op.HandleDigest = [];
                    op.CodeChallenge = ""; op.PendingPasswordHash = null; op.PendingPasswordSalt = null;
                    op.SourceSessionDigest = null;
                    op.ProtectedPendingTotpSecret = null; op.RecoveryCodeDigest = null; op.OutstandingRecoveryUserID = null;
                    op.OutstandingLinkSlot = null; op.Destination = null;
                    op.AppBinding = null; op.SessionAuthenticatedAt = null; op.SessionMfaSatisfied = null;
                    op.SessionLegacyCompatibilityExpiresAt = null;
                    unit.Audit("OperationExpired", now, op.ID);
                    return Task.FromResult(1);
                }, ct, requireClient: false);
        }
        var retentionCutoff = now.AddHours(-24);
        var tombstones = await db.Set<AuthenticationOperation>().Where(x =>
            (x.State == AuthenticationOperationState.Completed || x.State == AuthenticationOperationState.Cancelled ||
             x.State == AuthenticationOperationState.Locked || x.State == AuthenticationOperationState.Superseded) &&
            (((x.Purpose == AuthenticationOperationPurpose.LegacyRefreshExchange || x.Purpose == AuthenticationOperationPurpose.LegacyMfaExchange) && x.ExpiresAt <= now) ||
             (x.Purpose != AuthenticationOperationPurpose.LegacyRefreshExchange && x.Purpose != AuthenticationOperationPurpose.LegacyMfaExchange && x.CompletedAt < retentionCutoff)) &&
            !db.Set<UserSecurityState>().Any(s => s.MfaRecoveryOperationID == x.ID))
            .OrderBy(x => x.CompletedAt).Select(x => x.ID).Take(100).ToListAsync(ct);
        if (tombstones.Count > 0) await db.Set<AuthenticationOperation>().Where(x => tombstones.Contains(x.ID)).ExecuteDeleteAsync(ct);
        var oldBuckets = await db.Set<AuthThrottleBucket>().Where(x => x.WindowStart < retentionCutoff)
            .OrderBy(x => x.WindowStart).Select(x => x.Key).Take(100).ToListAsync(ct);
        if (oldBuckets.Count > 0)
            // A request can restart a bucket after selection. Never delete its refreshed window.
            await db.Set<AuthThrottleBucket>().Where(x => oldBuckets.Contains(x.Key) && x.WindowStart < retentionCutoff).ExecuteDeleteAsync(ct);
        return count;
    }
}
