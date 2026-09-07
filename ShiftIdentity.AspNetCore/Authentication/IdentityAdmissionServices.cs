using Microsoft.AspNetCore.DataProtection;
using ShiftSoftware.ShiftIdentity.Data.Authentication;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>Dependencies for the staged flow; deliberately absent from production registration.</summary>
internal sealed record IdentityAdmissionServices(
    IIdentitySecurityStore Store,
    AuthenticationClient Client,
    IdentityAdmissionOptions Options,
    TimeProvider Clock,
    ShiftSoftware.ShiftEntity.Core.IHashIdService HashIds,
    IDataProtector FactorProtector,
    AdmissionTokenCodec Tokens,
    Action<string>? Observe = null)
{
    internal Core.Authentication.NewPasswordPolicy PasswordPolicy { get; init; } = new();
}

internal sealed record IdentityAdmissionOptions(
    string Issuer, string RefreshAudience, byte[] AccessPrivateKey, byte[] RefreshKey,
    byte[] OperationKey, long PolicyRevision = 1, int AccessLifetimeSeconds = 900,
    int RefreshLifetimeSeconds = 2592000);

internal sealed record SessionProof(
    long UserID, long SecurityVersion, long PolicyRevision, long FactorGeneration,
    bool MfaSatisfied, DateTimeOffset AuthenticatedAt, string ClientID, string Audience, bool External, string Subject);

internal sealed record SignedInContext(SessionProof Proof, DateTimeOffset ExpiresAt);

/// <summary>Only the admission coordinator creates this after evaluating current state.</summary>
internal sealed record IssuanceDecision(
    SessionProof Proof, string Username, string FullName,
    IReadOnlyList<System.Security.Claims.Claim> Claims, DateTimeOffset AdmittedAt);
