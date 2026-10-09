using System.Collections.Generic;

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

    /// <summary>Sign in with Microsoft for existing accounts. Off unless <see cref="MicrosoftSignInSettings.Enabled"/>.</summary>
    public MicrosoftSignInSettings Microsoft { get; set; } = new();

    /// <summary>Sign in with Google for existing accounts. Off unless <see cref="GoogleSignInSettings.Enabled"/>.</summary>
    public GoogleSignInSettings Google { get; set; } = new();

    /// <summary>
    /// The screens that may sign in with a code (device sign-in, the OAuth 2.0 Device Authorization Grant): client ID
    /// to the display name the phone shows, for example <c>"service-screen": "Service Screen"</c>. A screen sends its
    /// client ID when it asks for a code; an ID that is not listed here is refused. Empty (the default) turns device
    /// sign-in off. A screen signs in as the person who approves it on a phone, with an ordinary session of this
    /// host's own client (<see cref="ClientId"/>), and renews it through <c>api/identity/v2/refresh</c>.
    /// A client ID is 1 to 64 letters, digits, dots, dashes or underscores.
    /// </summary>
    public Dictionary<string, string> DeviceClients { get; set; } = new();

    /// <summary>
    /// The absolute URL of the phone page that confirms a code, without a query, for example
    /// <c>https://identity.example/Identity/device</c>. The screen shows it, and its QR code adds <c>?code=</c> and the
    /// user code. Defaults to <c>FrontEndUrl</c> followed by <c>/Identity/device</c>; one of the two is required when
    /// <see cref="DeviceClients"/> names a client.
    /// </summary>
    public string? DeviceVerificationUri { get; set; }

    /// <summary>How long a device code and its user code stay valid, in seconds (60 to 1800). Defaults to 600.</summary>
    public int DeviceCodeLifetimeSeconds { get; set; } = 600;

    /// <summary>
    /// How often a screen may poll for its session, in seconds (1 to 60). Defaults to 5. A poll that comes sooner is
    /// answered with <c>slow_down</c>, and the screen then adds 5 seconds.
    /// </summary>
    public int DevicePollingIntervalSeconds { get; set; } = 5;
}

/// <summary>
/// Sign in with Microsoft: a work, school or personal Microsoft account whose email Microsoft verified signs in to the
/// existing account that has the same email, and marks that email verified. No account is ever created this way.
/// </summary>
public sealed class MicrosoftSignInSettings
{
    /// <summary>Turns Microsoft sign-in on for every account on this host. Off, the login screen shows no Microsoft button.</summary>
    public bool Enabled { get; set; }

    /// <summary>The Application (client) ID of the host's Microsoft Entra app registration.</summary>
    public string? ClientId { get; set; }

    /// <summary>A client secret of that app registration. Keep it in App Settings or Key Vault, never in a settings file.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// The absolute URL Microsoft returns to, registered on the app registration. Defaults to this API's
    /// <c>api/identity/v2/providers/microsoft/callback</c> as the request reached it (HTTPS except on localhost); set it
    /// when a proxy or custom domain makes that address differ from the registered one.
    /// </summary>
    public string? RedirectUri { get; set; }

    /// <summary>
    /// When true, a Microsoft sign-in still asks for the account's Shift authenticator code, or for enrollment where the
    /// host requires MFA, as a password sign-in does. When false (the default), Microsoft sign-in skips Shift MFA.
    /// </summary>
    public bool RequireShiftMfa { get; set; }
}

/// <summary>
/// Sign in with Google: a Google account whose email Google marks verified signs in to the existing account that has
/// the same email, and marks that email verified. No account is ever created this way.
/// </summary>
public sealed class GoogleSignInSettings
{
    /// <summary>Turns Google sign-in on for every account on this host. Off, the login screen shows no Google button.</summary>
    public bool Enabled { get; set; }

    /// <summary>The client ID of the host's Google OAuth client (a Web application client), ending in <c>.apps.googleusercontent.com</c>.</summary>
    public string? ClientId { get; set; }

    /// <summary>That OAuth client's secret. Keep it in App Settings or Key Vault, never in a settings file.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// The absolute URL Google returns to, registered on the OAuth client. Defaults to this API's
    /// <c>api/identity/v2/providers/google/callback</c> as the request reached it (HTTPS except on localhost); set it
    /// when a proxy or custom domain makes that address differ from the registered one.
    /// </summary>
    public string? RedirectUri { get; set; }

    /// <summary>
    /// When true, a Google sign-in still asks for the account's Shift authenticator code, or for enrollment where the
    /// host requires MFA, as a password sign-in does. When false (the default), Google sign-in skips Shift MFA.
    /// </summary>
    public bool RequireShiftMfa { get; set; }
}
