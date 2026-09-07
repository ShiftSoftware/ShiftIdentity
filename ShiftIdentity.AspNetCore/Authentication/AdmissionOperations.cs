using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using OtpNet;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using static ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.AdmissionRules;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>Shared operation binding, proof budgets and step transitions, always under SQL admission.</summary>
internal static class AdmissionOperations
{
    internal sealed record Reference(Guid ID, long UserID, byte[] Digest, AuthenticationOperationPurpose Purpose);

    internal static async Task<Reference?> ReadAsync(IdentityAdmissionServices services, string? handle,
        AuthenticationOperationPurpose purpose, CancellationToken ct)
    {
        if (!OperationCredential.TryRead(handle, services.Options.OperationKey, out var id, out var digest)) return null;
        var found = await services.Store.ReadOperationAsync(id, ct);
        return found is not null && found.Purpose == purpose && CryptographicOperations.FixedTimeEquals(found.HandleDigest, digest)
            ? new(id, found.UserID, digest, found.Purpose) : null;
    }

    internal static AuthenticationRefused? Check(IdentityAdmissionServices services, IdentitySecurityTransaction unit,
        Reference reference, string verifier, AuthenticationOperationPurpose purpose, params AuthenticationOperationState[] states)
    {
        var op = unit.Operation;
        if (op is null || op.ID != reference.ID || op.UserID != unit.User.ID || op.Purpose != purpose ||
            !CryptographicOperations.FixedTimeEquals(op.HandleDigest, reference.Digest) ||
            op.ClientID != services.Client.ID || op.Audience != services.Client.Audience || op.External != services.Client.External ||
            !OperationCredential.VerifyChallenge(op.CodeChallenge, verifier)) return Refuse(AuthenticationFailure.InvalidGrant);
        if (op.State == AuthenticationOperationState.Locked) return Refuse(AuthenticationFailure.AttemptsExhausted);
        if (!states.Contains(op.State)) return Refuse(AuthenticationFailure.InvalidGrant);
        var now = services.Clock.GetUtcNow();
        if (now >= op.ExpiresAt || now < op.CreatedAt) return Refuse(AuthenticationFailure.Expired);
        var common = CommonRefusal(services, unit, op.SecurityVersion, op.PolicyRevision);
        if (common is not null) return common;
        if (unit.Security.FactorGeneration != op.FactorGeneration) return Refuse(AuthenticationFailure.StaleOperation);
        if (purpose == AuthenticationOperationPurpose.PasswordChange && op.PasswordChangeOrigin is not
            (PasswordChangeOrigin.Voluntary or PasswordChangeOrigin.RequiredLogin)) return Refuse(AuthenticationFailure.InvalidGrant);
        if (op.PasswordProvenAt is { } proven && (proven < op.CreatedAt || proven > now || now >= proven.AddMinutes(5)))
            return Refuse(AuthenticationFailure.Expired);
        if (op.MfaProvenAt is { } mfa && (op.PasswordProvenAt is not { } password || mfa < password || mfa > now))
            return Refuse(AuthenticationFailure.Expired);
        if (BudgetExhausted(unit.Security, now) || op.FailedAttempts >= 5 || unit.User.LockDownUntil > now.UtcDateTime)
            return Refuse(AuthenticationFailure.AttemptsExhausted);
        return null;
    }

    internal static ChallengeRequired Create(IdentityAdmissionServices services, IdentitySecurityTransaction unit,
        AuthenticationOperationPurpose purpose, AuthenticationOperationState state, string codeChallenge,
        DateTimeOffset createdAt, DateTimeOffset expiresAt, PasswordChangeOrigin? origin = null, DateTimeOffset? passwordProvenAt = null)
    {
        var credential = OperationCredential.Create(services.Options.OperationKey);
        var op = new AuthenticationOperation
        {
            ID = credential.ID, UserID = unit.User.ID, Purpose = purpose, State = state,
            SecurityVersion = unit.Security.SecurityVersion, FactorGeneration = unit.Security.FactorGeneration,
            PolicyRevision = unit.Policy.Revision, ClientID = services.Client.ID, Audience = services.Client.Audience,
            External = services.Client.External, HandleDigest = credential.Digest, CodeChallenge = codeChallenge,
            CreatedAt = createdAt, ExpiresAt = expiresAt, PasswordChangeOrigin = origin, PasswordProvenAt = passwordProvenAt
        };
        unit.AddOperation(op);
        unit.Audit(purpose == AuthenticationOperationPurpose.Login ? "LoginChallenge" : "PasswordChangeStarted", createdAt, op.ID);
        return Challenge(op, credential.Handle);
    }

    internal static ChallengeRequired Advance(IdentityAdmissionServices services, AuthenticationOperation op, AuthenticationOperationState state)
    {
        var credential = OperationCredential.Rotate(op.ID, services.Options.OperationKey);
        op.HandleDigest = credential.Digest;
        op.State = state;
        return Challenge(op, credential.Handle);
    }

    internal static AuthenticationRefused? VerifyMfa(IdentityAdmissionServices services, IdentitySecurityTransaction unit, string code)
    {
        var op = unit.Operation!;
        if (unit.Security.ProtectedTotpSecret is null || !unit.Policy.MfaEnabled || unit.Security.LocalMfaRecoveryRequired)
            return Refuse(AuthenticationFailure.StaleOperation);
        var now = services.Clock.GetUtcNow();
        var secret = services.FactorProtector.Unprotect(unit.Security.ProtectedTotpSecret);
        bool valid;
        long matchedStep;
        try
        {
            valid = new Totp(secret, unit.Policy.TotpPeriodSeconds, totpSize: unit.Policy.TotpDigits)
                .VerifyTotp(now.UtcDateTime, code, out matchedStep, new VerificationWindow(unit.Policy.TotpWindowPast, unit.Policy.TotpWindowFuture));
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
        if (!valid || matchedStep <= (unit.Security.LastAcceptedTotpStep ?? -1))
        {
            FailedAttempt(unit, now, "InvalidMfa");
            return Refuse(AuthenticationFailure.InvalidProof);
        }
        unit.Security.LastAcceptedTotpStep = matchedStep;
        op.MfaProvenAt = now;
        unit.Audit("MfaCompleted", now, op.ID);
        return null;
    }

    internal static void FailedAttempt(IdentitySecurityTransaction unit, DateTimeOffset now, string outcome)
    {
        var op = unit.Operation!;
        op.FailedAttempts++;
        if (op.FailedAttempts >= 5)
        {
            op.State = AuthenticationOperationState.Locked;
            ClearPreparedPassword(op);
        }
        FailedProof(unit.Security, now);
        unit.Audit(outcome, now, op.ID);
    }

    internal static void Finish(AuthenticationOperation op, DateTimeOffset now, bool cancelled = false)
    {
        op.State = cancelled ? AuthenticationOperationState.Cancelled : AuthenticationOperationState.Completed;
        op.CompletedAt = now;
        op.HandleDigest = [];
        op.CodeChallenge = "";
        ClearPreparedPassword(op);
    }

    private static void ClearPreparedPassword(AuthenticationOperation op)
    {
        op.PendingPasswordHash = null;
        op.PendingPasswordSalt = null;
    }

    private static ChallengeRequired Challenge(AuthenticationOperation op, string handle) => new(new(
        op.State switch
        {
            AuthenticationOperationState.AwaitingPassword => AuthenticationStep.Password,
            AuthenticationOperationState.AwaitingNewPassword => AuthenticationStep.PasswordChange,
            AuthenticationOperationState.AwaitingMfa => AuthenticationStep.ExistingMfa,
            _ => throw new InvalidOperationException("This operation has no pending step.")
        }, handle, op.ExpiresAt, op.Purpose));
}
