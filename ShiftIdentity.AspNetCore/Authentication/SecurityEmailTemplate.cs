using System.Globalization;
using System.Net;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>Shared, self-contained HTML and plain text. No remote images, tracking or client-side secret decoding.</summary>
public static class SecurityEmailTemplate
{
    public static SecurityEmailContent Render(SecurityEmail message, string frontEndUrl) => Compose(message.ID,
        message.Destination, message.FullName, message.Username, message.Purpose, Link(message, frontEndUrl), message.ExpiresAt);

    internal static string Link(SecurityEmail message, string frontEndUrl)
    {
        ValidateFrontEndUrl(frontEndUrl);
        var page = message.Purpose switch
        {
            AuthenticationOperationPurpose.EmailVerify => "VerifyEmail",
            AuthenticationOperationPurpose.PasswordResetEmail => "ResetPassword",
            _ => throw new InvalidOperationException("This purpose cannot be delivered by email.")
        };
        return frontEndUrl.TrimEnd('/') + "/Identity/" + page
            + "#grant=" + Uri.EscapeDataString(message.Grant) + "&purpose=" + message.Purpose;
    }

    internal static void ValidateFrontEndUrl(string? url)
    {
        if (url is null || !IdentityAuthorityRegistration.IsWebUrl(url)
            || new Uri(url).Query.Length != 0 || new Uri(url).Fragment.Length != 0)
            throw new InvalidOperationException("The identity authority's host sender adapter requires ShiftIdentityConfiguration.FrontEndUrl: an absolute HTTP(S) dashboard URL without a query or fragment.");
    }

    /// <summary>
    /// Compatibility only, until the old provider interfaces are removed. Those interfaces carry no expiry;
    /// this template deliberately makes no deadline claim. It never creates or decodes a legacy grant.
    /// </summary>
    public static SecurityEmailContent RenderLegacy(string url, UserDataDTO user, AuthenticationOperationPurpose purpose)
    {
        if (!IdentityAuthorityRegistration.IsWebUrl(url)) throw new InvalidOperationException("A security email requires an HTTP(S) link without credentials.");
        return Compose(Guid.NewGuid(), user.Email ?? "", user.FullName ?? "", user.Username ?? "", purpose, url, null);
    }

    private static SecurityEmailContent Compose(Guid id, string destination, string fullName, string username,
        AuthenticationOperationPurpose purpose, string link, DateTimeOffset? expiresAt)
    {
        var verification = purpose switch
        {
            AuthenticationOperationPurpose.EmailVerify => true,
            AuthenticationOperationPurpose.PasswordResetEmail => false,
            _ => throw new InvalidOperationException("This purpose cannot be delivered by email.")
        };
        var title = verification ? "Verify your email address" : "Reset your password";
        var explanation = verification
            ? "Use the link below, then choose Verify my email to confirm this email address. Opening the link alone makes no changes."
            : "Use the link below to choose a new password. Opening the link alone does not change your password.";
        var expiry = expiresAt.HasValue
            ? "This link expires at " + expiresAt.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) + "."
            : "If this link has expired, request a new one.";
        var greeting = string.IsNullOrWhiteSpace(fullName) ? "Hello," : "Hello " + fullName + ",";
        const string caution = "If you did not request this email, you can ignore it. Keep this link private and do not forward this email.";
        static string H(string value) => WebUtility.HtmlEncode(value);
        var html = $"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="referrer" content="no-referrer"><title>{H(title)}</title></head>
            <body style="margin:0;background:#f2f5f9;color:#243247;font-family:Arial,sans-serif">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center" style="padding:32px 16px">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:600px;background:#ffffff;border:1px solid #dce3ec;border-radius:12px">
                  <tr><td style="padding:32px">
                    <p style="margin:0 0 16px;color:#52627a;font-size:12px;letter-spacing:2px">ACCOUNT SECURITY</p>
                    <h1 style="margin:0 0 24px;font-size:26px;line-height:1.3">{H(title)}</h1>
                    <p>{H(greeting)}</p>
                    <p>Username: <strong>{H(username)}</strong></p>
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
        var text = $"{title}\n\n{greeting}\nUsername: {username}\n\n{explanation}\n\n{link}\n\n{expiry}\n\n{caution}";
        return new(id, destination, title, html, text);
    }
}
