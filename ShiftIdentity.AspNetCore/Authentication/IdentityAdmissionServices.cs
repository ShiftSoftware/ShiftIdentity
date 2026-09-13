using ShiftSoftware.ShiftIdentity.Data.Authentication;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>Dependencies for the staged flow; deliberately absent from production registration.</summary>
internal sealed record IdentityAdmissionServices(
    IIdentitySecurityStore Store,
    AuthenticationClient Client,
    IdentityAdmissionOptions Options,
    TimeProvider Clock,
    ShiftSoftware.ShiftEntity.Core.IHashIdService HashIds,
    IdentityMaterialProtector FactorProtector,
    AdmissionTokenCodec Tokens,
    Action<string>? Observe = null)
{
    internal Core.Authentication.NewPasswordPolicy PasswordPolicy { get; init; } = new();
    internal SecurityDeliveryLimits DeliveryLimits { get; init; } = new();
    internal ISecurityEmailSink? EmailSink { get; init; }
    internal LegacyRefreshTokenCodec? LegacyRefreshTokens { get; init; }
    internal IdentityMaterialProtector LinkProtector => FactorProtector.CreateProtector("SecurityLinks.v1");
}

internal sealed record SecurityDeliveryLimits(int CooldownSeconds = 60, int PerUserPerHour = 5, int PublicPerIpPer15Minutes = 20,
    int LinkRequestsPerIpPer15Minutes = 60, int HandoffTimeoutMilliseconds = 3000,
    int ResultPersistenceTimeoutMilliseconds = 2000, int PublicPaddingMilliseconds = 300);

internal sealed record IdentityAdmissionOptions(
    string Issuer, string RefreshAudience, byte[] AccessPrivateKey, byte[] RefreshKey,
    byte[] OperationKey, long PolicyRevision = 1, int AccessLifetimeSeconds = 900,
    int RefreshLifetimeSeconds = 2592000);

internal sealed record SessionProof(
    long UserID, long SecurityVersion, long PolicyRevision, long FactorGeneration,
    bool MfaSatisfied, DateTimeOffset AuthenticatedAt, string ClientID, string Audience, bool External, string Subject,
    string? AppBinding = null, DateTimeOffset? LegacyCompatibilityExpiresAt = null);

internal sealed record SignedInContext(SessionProof Proof, DateTimeOffset ExpiresAt);

/// <summary>Only the admission coordinator creates this after evaluating current state.</summary>
internal sealed record IssuanceDecision(
    SessionProof Proof, string Username, string FullName,
    IReadOnlyList<System.Security.Claims.Claim> Claims, DateTimeOffset AdmittedAt,
    string? Email = null, string? Phone = null, string? Signature = null,
    ShiftSoftware.ShiftEntity.Model.Enums.CompanyTypes? CompanyType = null);
