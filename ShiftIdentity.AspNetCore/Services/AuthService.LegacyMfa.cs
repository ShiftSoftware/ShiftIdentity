using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using static ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.AdmissionRules;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

public partial class AuthService
{
    /// <summary>
    /// Completes the MFA step of a login the old issuer started before cutover. The pre-cutover temporary credential
    /// proves a password check at some instant inside its configured lifetime and nothing more: it binds no security
    /// version, factor generation or operation, and it carries no issuance time. Under the accepted one-time migration
    /// exception the first presentation stamps the current version once, into a row keyed by the credential digest;
    /// every presentation still needs a real current factor proof through the shared verifier, the row budget and the
    /// account budget both count, the completed row is the replay tombstone until the credential expiry, and the
    /// resulting session carries MFA proof but no authentication-time credit.
    /// </summary>
    internal static Task<AuthOutcome> CompleteLegacyMfaAsync(IdentityAdmissionServices services, string credential,
        LegacyTemporaryTokenCodec.Proof legacy, string? code, CancellationToken ct) => AtBoundary(async () =>
    {
        if (services.Client.External || services.LegacyTemporaryTokens is not { } legacyTokens || legacy.Purpose != AuthPurpose.Mfa)
            return Refuse(AuthenticationFailure.InvalidGrant);
        if (!TotpCode(code)) return Refuse(AuthenticationFailure.InvalidRequest);
        if (LegacySubject(services, legacy.Subject) is not { } userID) return Refuse(AuthenticationFailure.InvalidGrant);
        // The exact credential is digested, never stored; the digest keys the single row that follows it across hosts.
        var digest = HMACSHA256.HashData(services.Options.OperationKey, Encoding.UTF8.GetBytes(credential));
        services.Observe?.Invoke("LegacyMfaProof");
        return await services.Store.AdmitLegacyCredentialAsync<AuthOutcome>(userID, AuthenticationOperationPurpose.LegacyMfaExchange,
            digest, services.Client, unit =>
        {
            var now = services.Clock.GetUtcNow();
            // Time passes while waiting for the user lock. Revalidate the same credential before trusting it.
            var current = legacyTokens.Validate(credential);
            if (current is null || current.Subject != legacy.Subject || current.Purpose != AuthPurpose.Mfa ||
                current.ExpiresAt != legacy.ExpiresAt || now >= legacy.ExpiresAt)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.Expired));
            var op = unit.Operation;
            if (op is null)
            {
                var refusal = LegacyCredentialRefusal(services, unit);
                if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
                if (BudgetExhausted(unit.Security, now) || unit.User.LockDownUntil > now.UtcDateTime)
                    return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.AttemptsExhausted));
                var first = LocalStep(unit, false);
                if (first != AuthenticationStep.ExistingMfa)
                    return Task.FromResult<AuthOutcome>(first is { } step ? Restricted(step, now) : Refuse(AuthenticationFailure.StaleOperation));
                op = new AuthenticationOperation
                {
                    ID = Guid.NewGuid(), UserID = unit.User.ID, Purpose = AuthenticationOperationPurpose.LegacyMfaExchange,
                    State = AuthenticationOperationState.AwaitingMfa,
                    SecurityVersion = unit.Security.SecurityVersion, PolicyRevision = unit.Policy.Revision,
                    FactorGeneration = unit.Security.FactorGeneration, ClientID = services.Client.ID,
                    Audience = services.Client.Audience, External = false, HandleDigest = digest, CodeChallenge = "",
                    CreatedAt = now, ExpiresAt = legacy.ExpiresAt
                };
                unit.AddOperation(op);
                unit.Audit("LegacyMfaChallenge", now, op.ID);
            }
            else
            {
                if (op.UserID != unit.User.ID || op.Purpose != AuthenticationOperationPurpose.LegacyMfaExchange ||
                    op.ClientID != services.Client.ID || op.Audience != services.Client.Audience || op.External ||
                    !CryptographicOperations.FixedTimeEquals(op.HandleDigest, digest))
                    return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidGrant));
                if (op.State == AuthenticationOperationState.Locked)
                    return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.AttemptsExhausted));
                // A completed row is the replay tombstone; a cancelled row belonged to an expired credential.
                if (op.State != AuthenticationOperationState.AwaitingMfa)
                    return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidGrant));
                if (now >= op.ExpiresAt || now < op.CreatedAt)
                {
                    Settle(op, AuthenticationOperationState.Cancelled, now);
                    return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.Expired));
                }
                var refusal = CommonRefusal(services, unit, op.SecurityVersion, op.PolicyRevision);
                if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
                if (unit.Security.FactorGeneration != op.FactorGeneration)
                    return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
                if (BudgetExhausted(unit.Security, now) || op.FailedAttempts >= 5 || unit.User.LockDownUntil > now.UtcDateTime)
                    return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.AttemptsExhausted));
                var next = LocalStep(unit, false);
                if (next != AuthenticationStep.ExistingMfa)
                    return Task.FromResult<AuthOutcome>(next is { } step ? Restricted(step, now) : Refuse(AuthenticationFailure.StaleOperation));
            }
            var failure = AdmissionOperations.VerifyMfa(services, unit, code!, operation: op);
            if (failure is not null) return Task.FromResult<AuthOutcome>(failure);
            Settle(op, AuthenticationOperationState.Completed, now);
            unit.Audit("LegacyMfaExchanged", now, op.ID);
            var remaining = LocalStep(unit, true);
            if (remaining is { } gate) return Task.FromResult<AuthOutcome>(Restricted(gate, now));
            // A real factor was just proven. The password check behind the old credential has no trusted time,
            // so the session gets no authentication-time credit; sensitive flows ask for the password again.
            var session = new SessionProof(unit.User.ID, unit.Security.SecurityVersion, unit.Policy.Revision,
                unit.Security.FactorGeneration, true, DateTimeOffset.UnixEpoch, services.Client.ID,
                services.Client.Audience, false, services.HashIds.Encode<UserDTO>(unit.User.ID));
            return Task.FromResult(Issue(services, unit, session, now));
        }, ct);
    });

    /// <summary>Ends the bridge row but keeps its credential digest, so a replay finds the tombstone.</summary>
    private static void Settle(AuthenticationOperation op, AuthenticationOperationState state, DateTimeOffset now)
    {
        op.State = state;
        op.CompletedAt = now;
        op.CodeChallenge = "";
    }

    private static bool TotpCode(string? code) => code is { Length: >= 6 and <= 8 } && code.All(char.IsAsciiDigit);

    /// <summary>Both the hash-encoded subject of current credentials and the numeric subject of older ones.</summary>
    internal static long? LegacySubject(IdentityAdmissionServices services, string subject)
    {
        try
        {
            var userID = services.HashIds.Decode<UserDTO>(subject);
            if (userID <= 0 && !long.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out userID)) return null;
            return userID > 0 ? userID : null;
        }
        catch (Exception e) when (e is ArgumentException or FormatException or OverflowException)
        {
            return null;
        }
    }
}
