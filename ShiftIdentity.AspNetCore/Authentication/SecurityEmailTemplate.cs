using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>The authority's security emails: the link for the admitted grant, composed by <see cref="SecurityEmailLayout"/>.</summary>
public static class SecurityEmailTemplate
{
    public static SecurityEmailContent Render(SecurityEmail message, string frontEndUrl) => Render(message, frontEndUrl, null);

    /// <summary>The same, with the logo at <paramref name="logoUrl"/> above the message, or none when it is null.</summary>
    public static SecurityEmailContent Render(SecurityEmail message, string frontEndUrl, string? logoUrl)
    {
        if (message.Purpose != AuthenticationOperationPurpose.ProviderLogin && !message.MicrosoftOnly)
            return Compose(message.ID, message.Destination, message.FullName, message.Username, message.Purpose,
                Link(message, frontEndUrl), message.ExpiresAt, logoUrl, message.Language);
        // A notice carries no grant: its button opens the login screen.
        ValidateFrontEndUrl(frontEndUrl);
        var body = SecurityEmailLayout.Compose(new(message.Purpose, message.FullName, message.Username,
            frontEndUrl.TrimEnd('/') + "/Identity/login", null)
            { LogoSource = logoUrl, ProviderAccount = message.ProviderAccount, Provider = message.Provider, MicrosoftOnly = message.MicrosoftOnly, Language = message.Language });
        return new(message.ID, message.Destination, body.Subject, body.HtmlBody, body.TextBody);
    }

    internal static string Link(SecurityEmail message, string frontEndUrl)
    {
        ValidateFrontEndUrl(frontEndUrl);
        var page = message.Purpose switch
        {
            AuthenticationOperationPurpose.EmailVerify => "VerifyEmail",
            AuthenticationOperationPurpose.PasswordResetEmail => "ResetPassword",
            _ => throw new InvalidOperationException("This purpose cannot be delivered by email.")
        };
        // The language goes in the query, not the fragment: the page reads it before it opens the grant.
        var language = string.IsNullOrEmpty(message.Language) ? "" : "?lang=" + Uri.EscapeDataString(message.Language);
        return frontEndUrl.TrimEnd('/') + "/Identity/" + page + language
            + "#grant=" + Uri.EscapeDataString(message.Grant) + "&purpose=" + message.Purpose;
    }

    /// <summary>The requested culture when it is a well-formed name such as "ru" or "ar-IQ"; otherwise null.</summary>
    internal static string? WellFormedLanguage(string? language) =>
        language is { Length: <= 35 } && System.Text.RegularExpressions.Regex.IsMatch(language,
            @"\A[A-Za-z]{2,3}(-[A-Za-z0-9]{1,8}){0,3}\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant) ? language : null;

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
        AuthenticationOperationPurpose purpose, string link, DateTimeOffset? expiresAt, string? logoUrl = null, string? language = null)
    {
        var body = SecurityEmailLayout.Compose(new(purpose, fullName, username, link, expiresAt) { LogoSource = logoUrl, Language = language });
        return new(id, destination, body.Subject, body.HtmlBody, body.TextBody);
    }
}
