using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Authentication;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>
/// Awaited before a host that enabled the authority serves: visits every user and copies a legacy plaintext factor into
/// the protected column. It runs after <see cref="IdentityAuthorityStartup"/>, which has created the security row of
/// every user that had none; a row still missing here stops the host.
/// </summary>
internal sealed class LegacyTotpMigration(IServiceScopeFactory scopes, ILogger<LegacyTotpMigration> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        long cursor = 0;
        var examined = 0;
        var copied = 0;
        try
        {
            while (true)
            {
                await using var scope = scopes.CreateAsyncScope();
                var protector = scope.ServiceProvider.GetRequiredService<IdentityMaterialProtector>();
                var db = scope.ServiceProvider.GetRequiredService<ShiftIdentityDbContext>();
                var batch = await new SqlIdentitySecurityStore(db).MigrateLegacyTotpBatchAsync(cursor, 100,
                    (state, secret) => CopyOrVerify(protector, state, secret), cancellationToken);
                examined += batch.Examined;
                copied += batch.Copied;
                cursor = batch.LastUserID;
                if (batch.Examined == 0) break;
            }
            logger.LogInformation("Identity factor migration completed: {Examined} users examined, {Copied} factors copied.", examined, copied);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            // SQL and cryptographic exception payloads must not expose credentials or configured key values.
            logger.LogError("Identity factor migration failed after user {Cursor}. Startup stopped; check schema readiness, factor keys and that user's saved identifiers.", cursor);
            throw new InvalidOperationException("Identity factor migration failed. Check schema readiness and factor keys.");
        }
    }

    internal static void CopyOrVerify(IdentityMaterialProtector protector, UserSecurityState state, byte[]? legacySecret)
    {
        if (state.ProtectedTotpSecret is not null)
        {
            // A newer enrolled factor can differ from the legacy column. Never overwrite it from that column.
            var existing = MfaMaterial.ReadActive(protector, state);
            CryptographicOperations.ZeroMemory(existing);
            return;
        }
        // Recovery deliberately removed the factor. The retained legacy column must not restore it.
        if (state.LocalMfaRecoveryRequired || legacySecret is null) return;
        if (legacySecret.Length == 0) throw new CryptographicException("Empty legacy factor.");
        MfaMaterial.ProtectActive(protector, state, legacySecret);
        var verified = MfaMaterial.ReadActive(protector, state);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(legacySecret, verified))
                throw new CryptographicException("Factor copy verification failed.");
        }
        finally { CryptographicOperations.ZeroMemory(verified); }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
