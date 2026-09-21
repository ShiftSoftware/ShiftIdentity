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

    internal static bool IsWebUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo);

    internal static IdentityAuthorityRegistration Create(ShiftIdentityConfiguration configuration)
    {
        if (configuration.EmailVerificationRedirectUrl is { } redirect && !IsWebUrl(redirect))
            throw Invalid("EmailVerificationRedirectUrl", "must be an absolute HTTP(S) URL without credentials.");
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
        var audience = First(settings.Audience, token.Audience, issuer);
        var refreshAudience = First(settings.RefreshAudience, configuration.RefreshToken?.Audience, issuer);
        var options = new IdentityAdmissionOptions(issuer, refreshAudience, accessKey, refreshKey, operationKey,
            AccessLifetimeSeconds: accessLifetime, RefreshLifetimeSeconds: refreshLifetime,
            AdministratorAuthenticationGraceSeconds: settings.AdministratorAuthenticationGraceSeconds);
        return new(options, new AuthenticationClient(clientID, audience), configuration);
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
