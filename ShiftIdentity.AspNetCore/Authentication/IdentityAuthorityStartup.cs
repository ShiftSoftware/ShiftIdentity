using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>
/// Runs first when a host that enabled the authority starts, before the factor visitor and before any request:
/// checks that the security schema exists, applies the configured policy to the single policy row (creating it, or
/// advancing its revision when the configured MFA or verified-email policy changed since the last start), creates the
/// App row of the host's own client when it is missing, creates the security row of every user that has none (the
/// expansion of the users that existed before the authority), and hands the policy revision the database holds to
/// the issuance options. A host whose migration is pending stops here with a message that says so.
/// </summary>
internal sealed class IdentityAuthorityStartup(IServiceScopeFactory scopes, IdentityAuthorityRegistration registration,
    ILogger<IdentityAuthorityStartup> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            CheckAdapters(scope.ServiceProvider);
            var db = scope.ServiceProvider.GetRequiredService<ShiftIdentityDbContext>();
            var revision = await EnsurePolicyAsync(db, registration, cancellationToken);
            var created = await EnsureClientAsync(db, registration, cancellationToken);
            var expanded = await UserSecurityExpansion.ExpandMissingAsync(db,
                userID => logger.LogWarning("Identity user {UserID} got its security row without lookup keys: its saved identifiers collide with another account's. Correct the identifiers so that the account can be found by the security-email lookup.", userID),
                cancellationToken);
            registration.Options = registration.Options with { PolicyRevision = revision };
            logger.LogInformation("Identity authority ready: policy revision {Revision}, client {ClientId}{Created}, {Expanded} security rows created.",
                revision, registration.Client.ID, created ? " (App row created)" : "", expanded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is SqlException or DbUpdateException)
        {
            logger.LogError("The identity authority could not start: the identity security schema or its rows are not ready. Apply the host's pending migration (the ShiftIdentity security tables) and start again.");
            throw new InvalidOperationException("The identity authority could not start. Apply the identity security migration and check the authority configuration.", e);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static void CheckAdapters(IServiceProvider services)
    {
        var missing = new List<string>();
        var registered = services.GetRequiredService<IServiceProviderIsService>();
        if (!registered.IsService(typeof(Data.Services.IUserAccountAuthority))) missing.Add("IUserAccountAuthority (administrator writers and verification delivery)");
        if (!registered.IsService(typeof(IIdentitySecurityStore))) missing.Add(nameof(IIdentitySecurityStore));
        if (!registered.IsService(typeof(IdentityAdmissionServices))) missing.Add(nameof(IdentityAdmissionServices));
        if (!registered.IsService(typeof(ISecurityEmailSink))) missing.Add(nameof(ISecurityEmailSink));
        if (missing.Count > 0)
            throw new InvalidOperationException("The identity authority cannot start. Missing adapter: " + string.Join(", ", missing) + ".");
        if (services.GetRequiredService<ISecurityEmailSink>() is HostSecurityEmailSink adapter) adapter.CheckReady();
    }

    /// <summary>The single policy row follows configuration; a change advances the revision, which ends every session bound to the old one.</summary>
    internal static async Task<long> EnsurePolicyAsync(ShiftIdentityDbContext db, IdentityAuthorityRegistration registration, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            db.ChangeTracker.Clear();
            var policy = await db.Set<AuthenticationPolicyState>().SingleOrDefaultAsync(x => x.ID == 1, ct);
            try
            {
                if (policy is null)
                {
                    policy = new() { MfaEnabled = registration.MfaEnabled, MfaMandatory = registration.MfaMandatory, RequireVerifiedEmail = registration.RequireVerifiedEmail,
                        TotpDigits = registration.Totp.Digits, TotpPeriodSeconds = registration.Totp.Period,
                        TotpWindowPast = registration.Totp.VerificationWindowPast, TotpWindowFuture = registration.Totp.VerificationWindowFuture };
                    db.Add(policy);
                    await db.SaveChangesAsync(ct);
                    return policy.Revision;
                }
                if (policy.MfaEnabled == registration.MfaEnabled && policy.MfaMandatory == registration.MfaMandatory &&
                    policy.RequireVerifiedEmail == registration.RequireVerifiedEmail && policy.TotpDigits == registration.Totp.Digits &&
                    policy.TotpPeriodSeconds == registration.Totp.Period && policy.TotpWindowPast == registration.Totp.VerificationWindowPast &&
                    policy.TotpWindowFuture == registration.Totp.VerificationWindowFuture)
                    return policy.Revision;
                policy.MfaEnabled = registration.MfaEnabled;
                policy.MfaMandatory = registration.MfaMandatory;
                policy.RequireVerifiedEmail = registration.RequireVerifiedEmail;
                policy.TotpDigits = registration.Totp.Digits;
                policy.TotpPeriodSeconds = registration.Totp.Period;
                policy.TotpWindowPast = registration.Totp.VerificationWindowPast;
                policy.TotpWindowFuture = registration.Totp.VerificationWindowFuture;
                policy.Revision = checked(policy.Revision + 1);
                await db.SaveChangesAsync(ct);
                return policy.Revision;
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                // Another instance of this host created or changed the row first; read it again.
            }
        }
    }

    /// <summary>The host's own client must exist as an App row: every session the deployed login issues is bound to it.</summary>
    internal static async Task<bool> EnsureClientAsync(ShiftIdentityDbContext db, IdentityAuthorityRegistration registration, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var id = registration.Client.ID;
        if (await db.Apps.IgnoreQueryFilters().AnyAsync(x => x.AppId == id && !x.IsDeleted, ct)) return false;
        db.Apps.Add(new App
        {
            AppId = id, DisplayName = registration.ClientDisplayName, RedirectUri = registration.RedirectUri,
            Description = "The identity host's own client. The identity authority created this row at startup; every session the identity login issues is bound to it."
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (await db.Apps.IgnoreQueryFilters().AnyAsync(x => x.AppId == id && !x.IsDeleted, ct)) return false;
            throw;
        }
        return true;
    }
}
