using ShiftSoftware.ShiftIdentity.Data.Authentication;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>
/// Dependencies of the authority's flows. Registered by <c>AddShiftIdentityAuthority</c> when a host enables the
/// authority and absent otherwise, which is what the deployed routes test to keep their previous behaviour.
/// </summary>
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
    internal string? EmailVerificationRedirectUrl { get; init; }
    internal LegacyRefreshTokenCodec? LegacyRefreshTokens { get; init; }
    internal LegacyTemporaryTokenCodec? LegacyTemporaryTokens { get; init; }
    /// <summary>Sign in with Microsoft, when the host turned it on; null otherwise.</summary>
    internal MicrosoftSignIn? Microsoft { get; init; }
    /// <summary>Sign in with Google, when the host turned it on; null otherwise.</summary>
    internal GoogleSignIn? Google { get; init; }
    /// <summary>Device sign-in, when the host configured device clients; null otherwise.</summary>
    internal DeviceGrantOptions? Device { get; init; }
    /// <summary>The provider's sign-in when the host turned it on; null otherwise.</summary>
    internal ProviderSignIn? Provider(Core.Authentication.SignInProvider provider) => provider switch
    {
        Core.Authentication.SignInProvider.Microsoft => Microsoft,
        Core.Authentication.SignInProvider.Google => Google,
        _ => null
    };
    internal IdentityMaterialProtector LinkProtector => FactorProtector.CreateProtector("SecurityLinks.v1");
}

internal sealed record SecurityDeliveryLimits(int CooldownSeconds = 60, int PerUserPerHour = 5, int PublicPerIpPer15Minutes = 20,
    int LinkRequestsPerIpPer15Minutes = 60, int HandoffTimeoutMilliseconds = 3000,
    int ResultPersistenceTimeoutMilliseconds = 2000, int PublicPaddingMilliseconds = 300,
    int DeviceAuthorizationsPerIpPer15Minutes = 60, int DeviceLookupsPerIpPer15Minutes = 60,
    int DeviceLookupFailuresPerIpPer15Minutes = 10, int DeviceUnknownCodesPerIpPer15Minutes = 30);

/// <summary>
/// Device sign-in as the host configured it: the device clients (client ID to the name the phone shows), the phone
/// page the screen shows, how long a code lives and how often a screen may poll.
/// </summary>
internal sealed record DeviceGrantOptions(IReadOnlyDictionary<string, string> Clients, string VerificationUri,
    int LifetimeSeconds = 600, int IntervalSeconds = 5);

internal sealed record IdentityAdmissionOptions(
    string Issuer, string RefreshAudience, byte[] AccessPrivateKey, byte[] RefreshKey,
    byte[] OperationKey, long PolicyRevision = 1, int AccessLifetimeSeconds = 900,
    int RefreshLifetimeSeconds = 2592000,
    int AdministratorAuthenticationGraceSeconds = Core.Authentication.AdministratorAuthentication.DefaultGracePeriodSeconds);

internal sealed record SessionProof(
    long UserID, long SecurityVersion, long PolicyRevision, long FactorGeneration,
    bool MfaSatisfied, DateTimeOffset AuthenticatedAt, string ClientID, string Audience, bool External, string Subject,
    string? AppBinding = null, DateTimeOffset? LegacyCompatibilityExpiresAt = null,
    Core.Authentication.SignInProvider? Provider = null);

internal sealed record SignedInContext(SessionProof Proof, DateTimeOffset ExpiresAt);

/// <summary>Only the admission coordinator creates this after evaluating current state.</summary>
internal sealed record IssuanceDecision(
    SessionProof Proof, string Username, string FullName,
    IReadOnlyList<System.Security.Claims.Claim> Claims, DateTimeOffset AdmittedAt,
    string? Email = null, string? Phone = null, string? Signature = null,
    ShiftSoftware.ShiftEntity.Model.Enums.CompanyTypes? CompanyType = null);
