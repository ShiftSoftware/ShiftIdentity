using System;
using System.Globalization;
using System.Net;

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
        var verification = fields.Purpose switch
        {
            AuthenticationOperationPurpose.EmailVerify => true,
            AuthenticationOperationPurpose.PasswordResetEmail => false,
            _ => throw new InvalidOperationException("This purpose cannot be delivered by email.")
        };
        var title = verification ? "Verify your email address" : "Reset your password";
        var explanation = verification
            ? "Use the link below, then choose Verify my email to confirm this email address. Opening the link alone makes no changes."
            : "Use the link below to choose a new password. Opening the link alone does not change your password.";
        var expiry = fields.ExpiresAt.HasValue
            ? "This link expires at " + fields.ExpiresAt.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) + "."
            : "If this link has expired, request a new one.";
        var greeting = string.IsNullOrWhiteSpace(fields.FullName) ? "Hello," : "Hello " + fields.FullName + ",";
        const string caution = "If you did not request this email, you can ignore it. Keep this link private and do not forward this email.";
        static string H(string value) => WebUtility.HtmlEncode(value);
        var link = fields.Link;
        var logo = string.IsNullOrWhiteSpace(fields.LogoSource) ? ""
            : $"""<p style="margin:0 0 24px"><img src="{H(fields.LogoSource!)}" alt="" height="40" style="display:block;height:40px;width:auto;border:0"></p>""";
        var html = $"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="referrer" content="no-referrer"><title>{H(title)}</title></head>
            <body style="margin:0;background:#f2f5f9;color:#243247;font-family:Arial,sans-serif">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center" style="padding:32px 16px">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:600px;background:#ffffff;border:1px solid #dce3ec;border-radius:12px">
                  <tr><td style="padding:32px">
                    {logo}
                    <p style="margin:0 0 16px;color:#52627a;font-size:12px;letter-spacing:2px">ACCOUNT SECURITY</p>
                    <h1 style="margin:0 0 24px;font-size:26px;line-height:1.3">{H(title)}</h1>
                    <p>{H(greeting)}</p>
                    <p>Username: <strong>{H(fields.Username)}</strong></p>
                    <p style="line-height:1.6">{H(explanation)}</p>
                    <p style="margin:28px 0"><a href="{H(link)}" rel="noreferrer" style="display:inline-block;padding:14px 22px;background:#234fc7;color:#ffffff;text-decoration:none;border-radius:6px;font-weight:bold">{H(title)}</a></p>
                    <p style="font-weight:bold">{H(expiry)}</p>
                    <p style="font-size:13px;line-height:1.6">If the button does not work, copy this link into your browser:</p>
                    <p style="font-size:12px;word-break:break-all"><a href="{H(link)}" rel="noreferrer" style="color:#234fc7">{H(link)}</a></p>
                    <hr style="border:0;border-top:1px solid #dce3ec;margin:28px 0">
                    <p style="font-size:13px;line-height:1.6;color:#52627a">{H(caution)}</p>
                  </td></tr>
                </table>
              </td></tr></table>
            </body></html>
            """;
        var text = $"{title}\n\n{greeting}\nUsername: {fields.Username}\n\n{explanation}\n\n{link}\n\n{expiry}\n\n{caution}";
        return new(title, html, text);
    }
}
