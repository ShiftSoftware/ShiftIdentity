using System.Security.Cryptography;
using System.Text;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data.Authentication;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>
/// The authority a host enabled: its client, its issuance options and the policy it applies at start. Built once
/// from <see cref="ShiftIdentityConfiguration"/>; its presence in the container is what registers and maps the
/// authority, and every request reads <see cref="Options"/> afresh because the startup job replaces the policy revision
/// with the one the database holds.
/// </summary>
internal sealed class IdentityAuthorityRegistration
{
    private IdentityAdmissionOptions options;

    private IdentityAuthorityRegistration(IdentityAdmissionOptions options, AuthenticationClient client, ShiftIdentityConfiguration configuration)
    {
        this.options = options;
        Client = client;
        var settings = configuration.Authority;
        MfaEnabled = configuration.MfaSettings?.Enabled ?? false;
        MfaMandatory = configuration.MfaSettings?.Mandatory ?? false;
        RequireVerifiedEmail = settings.RequireVerifiedEmail;
        Totp = configuration.MfaSettings?.Totp ?? new();
        ClientDisplayName = string.IsNullOrWhiteSpace(settings.ClientDisplayName) ? client.ID : settings.ClientDisplayName.Trim();
        RedirectUri = string.IsNullOrWhiteSpace(configuration.FrontEndUrl) ? "/" : configuration.FrontEndUrl.Trim();
    }

    public AuthenticationClient Client { get; }
    public IdentityAdmissionOptions Options { get => Volatile.Read(ref options); set => Volatile.Write(ref options, value); }
    public bool MfaEnabled { get; }
    public bool MfaMandatory { get; }
    public bool RequireVerifiedEmail { get; }
    public TotpSettingsModel Totp { get; }
    public string ClientDisplayName { get; }
    public string RedirectUri { get; }
    /// <summary>Why Microsoft sign-in stays off although enabled (no client secret yet), or null. Startup logs it.</summary>
    public string? MicrosoftProblem { get; private init; }
    /// <summary>Microsoft sign-in is enabled and has what it needs.</summary>
    public bool MicrosoftReady(MicrosoftSignInSettings? microsoft) => microsoft is { Enabled: true } && MicrosoftProblem is null;
    /// <summary>Why Google sign-in stays off although enabled (no client secret yet), or null. Startup logs it.</summary>
    public string? GoogleProblem { get; private init; }
    /// <summary>Google sign-in is enabled and has what it needs.</summary>
    public bool GoogleReady(GoogleSignInSettings? google) => google is { Enabled: true } && GoogleProblem is null;
    /// <summary>Device sign-in, when the host configured device clients; null otherwise.</summary>
    public DeviceGrantOptions? Device { get; private init; }

    internal static bool IsWebUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo);

    internal static IdentityAuthorityRegistration Create(ShiftIdentityConfiguration configuration)
    {
        if (configuration.EmailVerificationRedirectUrl is { } redirect && !IsWebUrl(redirect))
            throw Invalid("EmailVerificationRedirectUrl", "must be an absolute HTTP(S) URL without credentials.");
        if (configuration.EmailLogoUrl is { } logo && !IsWebUrl(logo))
            throw Invalid("EmailLogoUrl", "must be an absolute HTTP(S) URL without credentials.");
        var settings = configuration.Authority ?? throw Invalid("Authority", "is required when the authority is enabled.");
        if (!settings.Enabled) throw Invalid("Authority.Enabled", "must be true to register the authority.");
        var clientID = settings.ClientId?.Trim();
        if (string.IsNullOrEmpty(clientID) || clientID.Length > 255) throw Invalid("Authority.ClientId", "must name the identity host's own App row (1 to 255 characters).");
        var token = configuration.Token ?? throw Invalid("Token", "is required: the authority signs access tokens with the configured RSA key and issuer.");
        var issuer = token.Issuer?.Trim();
        if (string.IsNullOrEmpty(issuer)) throw Invalid("Token.Issuer", "is required.");
        byte[] accessKey;
        try
        {
            accessKey = Convert.FromBase64String(token.RSAPrivateKeyBase64 ?? "");
            using var rsa = RSA.Create();
            rsa.ImportRSAPrivateKey(accessKey, out _);
        }
        catch (Exception e) when (e is FormatException or CryptographicException or ArgumentNullException)
        { throw Invalid("Token.RSAPrivateKeyBase64", "must be the Base64 PKCS#1 RSA private key the deployed issuer already uses."); }
        try { _ = new IdentityMaterialProtector(configuration.FactorProtection); }
        catch (InvalidOperationException e) { throw Invalid("FactorProtection", $"must hold the authority's factor keys: {e.Message}"); }
        // The deployed refresh issuer uses the exact UTF-8 text, even when that text looks like Base64.
        // Preserve those bytes so ordinary host upgrades need neither another refresh secret nor key derivation.
        var refreshKey = settings.RefreshKey is null
            ? ExistingRefreshKey(configuration.RefreshToken?.Key)
            : KeyBytes(settings.RefreshKey, "Authority.RefreshKey", 64);
        var operationKey = KeyBytes(settings.OperationKey, "Authority.OperationKey", 32);
        var totp = configuration.MfaSettings?.Totp ?? new();
        if (totp.Digits is < 6 or > 8 || totp.Period is < 1 or > 300 ||
            totp.VerificationWindowPast is < 0 or > 2 || totp.VerificationWindowFuture is < 0 or > 2)
            throw Invalid("MfaSettings.Totp", "has an unsupported digits, period or verification window.");
        var accessLifetime = settings.AccessLifetimeSeconds ?? token.ExpireSeconds;
        if (accessLifetime is < 1 or > 900)
            throw Invalid("Authority.AccessLifetimeSeconds", $"must be between 1 and 900 seconds; the value in effect is {accessLifetime} (Token.ExpireSeconds applies when it is not set).");
        var refreshLifetime = settings.RefreshLifetimeSeconds ?? configuration.RefreshToken?.ExpireSeconds ?? 0;
        if (refreshLifetime < 1) throw Invalid("Authority.RefreshLifetimeSeconds", "must be at least 1 second (RefreshToken.ExpireSeconds applies when it is not set).");
        if (settings.AdministratorAuthenticationGraceSeconds < 1)
            throw Invalid("Authority.AdministratorAuthenticationGraceSeconds", "must be at least 1 second.");
        var microsoftProblem = ValidateMicrosoft(settings.Microsoft);
        var googleProblem = ValidateGoogle(settings.Google);
        var device = ValidateDevice(settings, configuration.FrontEndUrl);
        var audience = First(settings.Audience, token.Audience, issuer);
        var refreshAudience = First(settings.RefreshAudience, configuration.RefreshToken?.Audience, issuer);
        var options = new IdentityAdmissionOptions(issuer, refreshAudience, accessKey, refreshKey, operationKey,
            AccessLifetimeSeconds: accessLifetime, RefreshLifetimeSeconds: refreshLifetime,
            AdministratorAuthenticationGraceSeconds: settings.AdministratorAuthenticationGraceSeconds);
        return new(options, new AuthenticationClient(clientID, audience), configuration)
        { MicrosoftProblem = microsoftProblem, GoogleProblem = googleProblem, Device = device };
    }

    // Device sign-in is on when device clients are configured. A malformed client, phone page, lifetime or interval
    // fails at startup, not when the first screen asks for a code.
    internal static DeviceGrantOptions? ValidateDevice(AuthoritySettingsModel settings, string? frontEndUrl)
    {
        if (settings.DeviceClients is not { Count: > 0 } configured) return null;
        var clients = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, name) in configured)
        {
            if (!DeviceClientId.IsMatch(id ?? ""))
                throw Invalid("Authority.DeviceClients", $"has the client ID '{id}'. A client ID is 1 to 64 letters, digits, dots, dashes or underscores, starting with a letter or digit.");
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
                throw Invalid("Authority.DeviceClients", $"must give the client '{id}' a display name of 1 to 100 characters. The phone shows it.");
            clients.Add(id!, name.Trim());
        }
        var page = settings.DeviceVerificationUri?.Trim();
        if (string.IsNullOrEmpty(page))
        {
            var frontEnd = frontEndUrl?.Trim();
            if (string.IsNullOrEmpty(frontEnd) || !IsWebUrl(frontEnd))
                throw Invalid("Authority.DeviceVerificationUri", "is required when Authority.DeviceClients names a client: the absolute URL of the phone page, for example https://identity.example/Identity/device. FrontEndUrl followed by /Identity/device applies when it is not set.");
            page = frontEnd.TrimEnd('/') + "/Identity/device";
        }
        ValidateRedirect(page, "Authority.DeviceVerificationUri");
        if (settings.DeviceCodeLifetimeSeconds is < 60 or > 1800)
            throw Invalid("Authority.DeviceCodeLifetimeSeconds", "must be between 60 and 1800 seconds.");
        if (settings.DevicePollingIntervalSeconds is < 1 or > 60 || settings.DevicePollingIntervalSeconds >= settings.DeviceCodeLifetimeSeconds)
            throw Invalid("Authority.DevicePollingIntervalSeconds", "must be between 1 and 60 seconds and shorter than the code lifetime.");
        return new(clients.AsReadOnly(), page, settings.DeviceCodeLifetimeSeconds, settings.DevicePollingIntervalSeconds);
    }

    private static readonly System.Text.RegularExpressions.Regex DeviceClientId =
        new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    // Turned on, Microsoft sign-in needs its app registration: a malformed value fails at startup, not at the first
    // sign-in. A missing secret only keeps it off, with a startup warning, so a checked-in empty secret starts the host.
    internal static string? ValidateMicrosoft(MicrosoftSignInSettings? microsoft)
    {
        if (microsoft is not { Enabled: true }) return null;
        if (!Guid.TryParse(microsoft.ClientId?.Trim(), out _))
            throw Invalid("Authority.Microsoft.ClientId", "must be the Application (client) ID of the Microsoft Entra app registration when Microsoft sign-in is enabled.");
        ValidateRedirect(microsoft.RedirectUri, "Authority.Microsoft.RedirectUri");
        return string.IsNullOrWhiteSpace(microsoft.ClientSecret)
            ? "ShiftIdentityConfiguration.Authority.Microsoft.ClientSecret is empty; supply it from App Settings, Key Vault or user secrets."
            : null;
    }

    // The same for Google's OAuth client.
    internal static string? ValidateGoogle(GoogleSignInSettings? google)
    {
        if (google is not { Enabled: true }) return null;
        var clientId = google.ClientId?.Trim() ?? "";
        if (!clientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal) || clientId.Length == ".apps.googleusercontent.com".Length ||
            clientId.Any(x => x is < '!' or > '~'))
            throw Invalid("Authority.Google.ClientId", "must be the client ID of the Google OAuth client (ending in .apps.googleusercontent.com) when Google sign-in is enabled.");
        ValidateRedirect(google.RedirectUri, "Authority.Google.RedirectUri");
        return string.IsNullOrWhiteSpace(google.ClientSecret)
            ? "ShiftIdentityConfiguration.Authority.Google.ClientSecret is empty; supply it from App Settings, Key Vault or user secrets."
            : null;
    }

    private static void ValidateRedirect(string? redirect, string name)
    {
        if (redirect is not null && !string.IsNullOrWhiteSpace(redirect) &&
            (!IsWebUrl(redirect.Trim()) || new Uri(redirect.Trim()).Query.Length != 0 || new Uri(redirect.Trim()).Fragment.Length != 0))
            throw Invalid(name, "must be an absolute HTTP(S) URL without credentials, query or fragment.");
    }

    private static byte[] ExistingRefreshKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Invalid("RefreshToken.Key", "is required.");
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length < 64) throw Invalid("RefreshToken.Key", "must contain at least 64 UTF-8 bytes for HS512.");
        return bytes;
    }

    /// <summary>A configured key is Base64 when it parses as Base64, otherwise its UTF-8 text; either way at least <paramref name="minimum"/> bytes.</summary>
    internal static byte[] KeyBytes(string? value, string name, int minimum)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0) throw Invalid(name, $"is required: a Base64 or text key of at least {minimum} bytes.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(text); }
        catch (FormatException) { bytes = Encoding.UTF8.GetBytes(text); }
        if (bytes.Length < minimum) throw Invalid(name, $"must be at least {minimum} bytes; the configured key has {bytes.Length}.");
        return bytes;
    }

    private static string First(params string?[] values) => values.First(x => !string.IsNullOrWhiteSpace(x))!.Trim();

    private static InvalidOperationException Invalid(string setting, string requirement) =>
        new($"Identity authority configuration: ShiftIdentityConfiguration.{setting} {requirement}");
}
