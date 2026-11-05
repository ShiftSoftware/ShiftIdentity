using System;
using System.Globalization;
using System.Net;
using ShiftSoftwareLocalization.Identity;

namespace ShiftSoftware.ShiftIdentity.Core.Authentication;

/// <summary>What a security email says: the display fields of the admitted account and the link it carries.</summary>
public sealed record SecurityEmailFields(AuthenticationOperationPurpose Purpose, string FullName, string Username,
    string Link, DateTimeOffset? ExpiresAt)
{
    /// <summary>
    /// The image shown above the message, or null for none. The sender decides how it reaches the inbox, so this is
    /// any value an img src accepts there (an absolute HTTPS URL or a cid: reference).
    /// </summary>
    public string? LogoSource { get; init; }

    /// <summary>On the provider-link notice: the provider account that can now sign in.</summary>
    public string? ProviderAccount { get; init; }

    /// <summary>On the provider-link notice: the provider of that account. A notice without one is Microsoft's.</summary>
    public SignInProvider? Provider { get; init; }

    /// <summary>
    /// The notice sent instead of a reset link to an account that signs in only with Microsoft through its organization.
    /// <see cref="Link"/> opens the login screen.
    /// </summary>
    public bool MicrosoftOnly { get; init; }

    /// <summary>
    /// The culture to write the email in, such as "ru". English when it is null, or when there is no translation for it.
    /// </summary>
    public string? Language { get; init; }
}

/// <summary>A composed security email.</summary>
public sealed record SecurityEmailBody(string Subject, string HtmlBody, string TextBody);

/// <summary>
/// The layout of the verification and reset emails. Self-contained HTML and plain text, with no tracking or
/// client-side secret decoding. The identity authority sends it; the dashboard's preview pages render the same code.
/// </summary>
public static class SecurityEmailLayout
{
    public static SecurityEmailBody Compose(SecurityEmailFields fields)
    {
        if (fields.Purpose == AuthenticationOperationPurpose.ProviderLogin || fields.MicrosoftOnly) return ComposeProviderNotice(fields);
        var verification = fields.Purpose switch
        {
            AuthenticationOperationPurpose.EmailVerify => true,
            AuthenticationOperationPurpose.PasswordResetEmail => false,
            _ => throw new InvalidOperationException("This purpose cannot be delivered by email.")
        };
        var culture = Culture(fields.Language);
        string T(string key) => Text(key, culture);
        var title = T(verification ? "Verify your email address" : "Reset your password");
        var explanation = T(verification
            ? "Use the link below, then choose Verify my email to confirm this email address. Opening the link alone makes no changes."
            : "Use the link below to choose a new password. Opening the link alone does not change your password.");
        var expiry = fields.ExpiresAt.HasValue
            ? string.Format(culture, T("This link expires at {0}."), Date(fields.ExpiresAt.Value, fields.Language))
            : T("If this link has expired, request a new one.");
        var greeting = Greeting(fields.FullName, culture);
        var username = T("Username") + ": ";
        var caution = T("If you did not request this email, you can ignore it. Keep this link private and do not forward this email.");
        var document = DocumentLanguage(fields.Language, culture);
        static string H(string value) => WebUtility.HtmlEncode(value);
        var link = fields.Link;
        var logo = string.IsNullOrWhiteSpace(fields.LogoSource) ? ""
            : $"""<p style="margin:0 0 24px"><img src="{H(fields.LogoSource!)}" alt="" height="40" style="display:block;height:40px;width:auto;border:0"></p>""";
        var html = $"""
            <!doctype html>
            <html {document}><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="referrer" content="no-referrer"><title>{H(title)}</title></head>
            <body style="margin:0;background:#f2f5f9;color:#243247;font-family:Arial,sans-serif">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center" style="padding:32px 16px">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:600px;background:#ffffff;border:1px solid #dce3ec;border-radius:12px">
                  <tr><td style="padding:32px">
                    {logo}
                    <p style="margin:0 0 16px;color:#52627a;font-size:12px;letter-spacing:2px">{H(T("ACCOUNT SECURITY"))}</p>
                    <h1 style="margin:0 0 24px;font-size:26px;line-height:1.3">{H(title)}</h1>
                    <p>{H(greeting)}</p>
                    <p>{H(username)}<strong>{H(fields.Username)}</strong></p>
                    <p style="line-height:1.6">{H(explanation)}</p>
                    <p style="margin:28px 0"><a href="{H(link)}" rel="noreferrer" style="display:inline-block;padding:14px 22px;background:#234fc7;color:#ffffff;text-decoration:none;border-radius:6px;font-weight:bold">{H(title)}</a></p>
                    <p style="font-weight:bold">{H(expiry)}</p>
                    <p style="font-size:13px;line-height:1.6">{H(T("If the button does not work, copy this link into your browser:"))}</p>
                    <p style="font-size:12px;word-break:break-all"><a href="{H(link)}" rel="noreferrer" style="color:#234fc7">{H(link)}</a></p>
                    <hr style="border:0;border-top:1px solid #dce3ec;margin:28px 0">
                    <p style="font-size:13px;line-height:1.6;color:#52627a">{H(caution)}</p>
                  </td></tr>
                </table>
              </td></tr></table>
            </body></html>
            """;
        var text = $"{title}\n\n{greeting}\n{username}{fields.Username}\n\n{explanation}\n\n{link}\n\n{expiry}\n\n{caution}";
        return new(title, html, text);
    }

    // No language means English, whatever culture the server runs in: the neutral resources are English.
    private static CultureInfo Culture(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return CultureInfo.InvariantCulture;
        try { return CultureInfo.GetCultureInfo(language); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }

    private static string Text(string key, CultureInfo culture) => Resource.ResourceManager.GetString(key, culture) ?? key;

    private static string Greeting(string fullName, CultureInfo culture) => string.IsNullOrWhiteSpace(fullName)
        ? Text("Hello,", culture) : string.Format(culture, Text("Hello {0},", culture), fullName);

    /// <summary>
    /// The attributes of the html element. A language with no translation is written in English, so it is marked as English.
    /// </summary>
    private static string DocumentLanguage(string? language, CultureInfo culture)
    {
        const string sample = "Reset your password";
        if (string.IsNullOrWhiteSpace(language) || Text(sample, culture) == Text(sample, CultureInfo.InvariantCulture)) return "lang=\"en\"";
        return $"lang=\"{WebUtility.HtmlEncode(language)}\"" + (RightToLeft(language) ? " dir=\"rtl\"" : "");
    }

    /// <summary>
    /// Arabic and Kurdish read right to left. This names them because .NET treats "ku" as Latin-script Kurmanji, which reads
    /// left to right, but the Kurdish texts are Sorani in Arabic script.
    /// </summary>
    private static bool RightToLeft(string? language) =>
        language is not null && language.Split('-')[0].ToLowerInvariant() is "ar" or "ku";

    /// <summary>
    /// The deadline in one fixed format. In a right-to-left sentence, the numbers would otherwise be shown in another order
    /// (2031-02-03 as 03-02-2031), so unicode directional isolates keep this part of the text left to right.
    /// </summary>
    private static string Date(DateTimeOffset value, string? language)
    {
        var date = value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        return RightToLeft(language) ? "⁦" + date + "⁩" : date;
    }

    /// <summary>
    /// The notice sent when a provider account first signs in to an account. It carries no grant; its button opens the
    /// login screen. It names the provider account that can now sign in, so a mistaken email address shows at once.
    /// The same layout answers a reset request from an account that signs in only with Microsoft.
    /// </summary>
    private static SecurityEmailBody ComposeProviderNotice(SecurityEmailFields fields)
    {
        var culture = Culture(fields.Language);
        string T(string key) => Text(key, culture);
        var provider = fields.Provider == SignInProvider.Google ? "Google" : "Microsoft";
        var title = fields.MicrosoftOnly ? T("Your account signs in with Microsoft")
            : string.Format(culture, T("{0} sign-in linked to your account"), provider);
        var explanation = fields.MicrosoftOnly
            ? T("Someone asked to reset the password of your account. Your account has no password to reset: it signs in with your organization's Microsoft account. On the sign-in page, choose Sign in with Microsoft.")
            : string.IsNullOrWhiteSpace(fields.ProviderAccount)
                ? string.Format(culture, T("A {0} account just signed in to your account. From now on it can sign in without your password."), provider)
                : string.Format(culture, T("The {0} account {1} just signed in to your account. From now on it can sign in without your password."), provider, fields.ProviderAccount);
        var caution = T(fields.MicrosoftOnly
            ? "If you did not ask for this, you can ignore this email. Nothing about your account has changed."
            : "If this was not you, contact your administrator now: your email address may be on the wrong account.");
        var greeting = Greeting(fields.FullName, culture);
        var username = T("Username") + ": ";
        var signIn = T("Go to sign in");
        var document = DocumentLanguage(fields.Language, culture);
        static string H(string value) => WebUtility.HtmlEncode(value);
        var logo = string.IsNullOrWhiteSpace(fields.LogoSource) ? ""
            : $"""<p style="margin:0 0 24px"><img src="{H(fields.LogoSource!)}" alt="" height="40" style="display:block;height:40px;width:auto;border:0"></p>""";
        var html = $"""
            <!doctype html>
            <html {document}><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="referrer" content="no-referrer"><title>{H(title)}</title></head>
            <body style="margin:0;background:#f2f5f9;color:#243247;font-family:Arial,sans-serif">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center" style="padding:32px 16px">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:600px;background:#ffffff;border:1px solid #dce3ec;border-radius:12px">
                  <tr><td style="padding:32px">
                    {logo}
                    <p style="margin:0 0 16px;color:#52627a;font-size:12px;letter-spacing:2px">{H(T("ACCOUNT SECURITY"))}</p>
                    <h1 style="margin:0 0 24px;font-size:26px;line-height:1.3">{H(title)}</h1>
                    <p>{H(greeting)}</p>
                    <p>{H(username)}<strong>{H(fields.Username)}</strong></p>
                    <p style="line-height:1.6">{H(explanation)}</p>
                    <p style="margin:28px 0"><a href="{H(fields.Link)}" rel="noreferrer" style="display:inline-block;padding:14px 22px;background:#234fc7;color:#ffffff;text-decoration:none;border-radius:6px;font-weight:bold">{H(signIn)}</a></p>
                    <hr style="border:0;border-top:1px solid #dce3ec;margin:28px 0">
                    <p style="font-size:13px;line-height:1.6;color:#52627a">{H(caution)}</p>
                  </td></tr>
                </table>
              </td></tr></table>
            </body></html>
            """;
        var text = $"{title}\n\n{greeting}\n{username}{fields.Username}\n\n{explanation}\n\n{signIn}: {fields.Link}\n\n{caution}";
        return new(title, html, text);
    }
}
