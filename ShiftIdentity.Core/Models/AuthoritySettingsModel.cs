namespace ShiftSoftware.ShiftIdentity.Core.Models;

/// <summary>
/// The identity authority: SQL-admitted logins, versioned sessions, protected authenticators and the account flows
/// under <c>api/identity/v2</c>. <c>AddShiftIdentityDashboard</c> registers it when <see cref="Enabled"/> is true;
/// a host that leaves it off keeps the previous issuer and writers unchanged. The access tokens it issues are signed
/// with the same RSA key and issuer as before, so every deployed consumer validates them without a change.
/// </summary>
public sealed class AuthoritySettingsModel
{
    /// <summary>Turns the authority on. Off, nothing is registered and nothing is mapped.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The <c>AppId</c> of the App row that represents the identity host's own client. Every session the deployed
    /// login issues is bound to it. The row is created at startup when it does not exist.
    /// </summary>
    public string ClientId { get; set; } = default!;

    /// <summary>The display name of that App row when the authority creates it; defaults to <see cref="ClientId"/>.</summary>
    public string? ClientDisplayName { get; set; }

    /// <summary>
    /// The audience written into access tokens and checked by the authority's own bearer routes. Deployed consumers
    /// do not validate an audience. Defaults to the configured token audience, then to the issuer.
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>The audience of the authority's refresh tokens. Defaults to the configured refresh-token audience.</summary>
    public string? RefreshAudience { get; set; }

    /// <summary>
    /// Optional override for hosts that already configured a separate authority key. When omitted, refresh tokens
    /// use the existing RefreshToken.Key with its original UTF-8 encoding. Token schema and purpose distinguish
    /// new credentials from legacy credentials; a separate secret is not required.
    /// An explicit override remains Base64 or plain text, at least 64 bytes.
    /// </summary>
    public string? RefreshKey { get; set; }

    /// <summary>Keys the authority's operation handles and throttle digests: Base64 or plain text, at least 32 bytes.</summary>
    public string OperationKey { get; set; } = default!;

    /// <summary>
    /// Access-token lifetime in seconds, at most 900. Defaults to the configured token lifetime; a longer configured
    /// lifetime must be replaced by an explicit value here, because the authority never issues a longer one.
    /// </summary>
    public int? AccessLifetimeSeconds { get; set; }

    /// <summary>Refresh-token lifetime in seconds. Defaults to the configured refresh-token lifetime.</summary>
    public int? RefreshLifetimeSeconds { get; set; }

    /// <summary>
    /// Maximum age of actual password and applicable MFA authentication for protected administrator actions.
    /// Defaults to 20 hours; must be positive. Read at startup. Token renewal never extends this interval.
    /// This does not change the separate five-minute confirmation-operation deadline.
    /// </summary>
    public int AdministratorAuthenticationGraceSeconds { get; set; } = Authentication.AdministratorAuthentication.DefaultGracePeriodSeconds;

    /// <summary>
    /// Refuses a local login until the account's saved email address is verified. Together with the MFA settings this
    /// is the authority's policy; a change is applied at the next start and ends every session bound to the old policy.
    /// </summary>
    public bool RequireVerifiedEmail { get; set; }
}
