using System.Net;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Policy")]
public sealed class SecurityEmailTemplateTests
{
    [Theory]
    [InlineData(AuthenticationOperationPurpose.EmailVerify, "VerifyEmail", "Verify your email address")]
    [InlineData(AuthenticationOperationPurpose.PasswordResetEmail, "ResetPassword", "Reset your password")]
    public void Rendering_encodes_display_text_and_URL_and_uses_the_admitted_deadline(AuthenticationOperationPurpose purpose, string page, string subject)
    {
        var expiry = new DateTimeOffset(2031, 2, 3, 4, 5, 6, TimeSpan.FromHours(3));
        var message = new SecurityEmail(Guid.NewGuid(), "admitted@example.invalid", "ignored", "a&b/+\"<>", purpose, expiry)
        { FullName = "<img src=x onerror='bad'> & Name", Username = "\"<script>name</script>" };
        var content = SecurityEmailTemplate.Render(message, "https://dashboard.example.invalid/a&b/");
        var link = $"https://dashboard.example.invalid/a&b/Identity/{page}#grant=a%26b%2F%2B%22%3C%3E&purpose={purpose}";
        Assert.Equal(message.ID, content.ID); Assert.Equal(message.Destination, content.Destination); Assert.Equal(subject, content.Subject);
        Assert.Contains(WebUtility.HtmlEncode(message.FullName), content.HtmlBody);
        Assert.Contains(WebUtility.HtmlEncode(message.Username), content.HtmlBody);
        Assert.Contains("href=\"" + WebUtility.HtmlEncode(link) + "\"", content.HtmlBody);
        Assert.DoesNotContain("<img", content.HtmlBody); Assert.DoesNotContain("<script>", content.HtmlBody);
        Assert.Contains("2031-02-03 01:05:06 UTC", content.HtmlBody);
        Assert.Contains("2031-02-03 01:05:06 UTC", content.TextBody);
        Assert.Contains(link, content.TextBody); Assert.Contains(message.FullName, content.TextBody); Assert.Contains(message.Username, content.TextBody);
        Assert.Contains("no-referrer", content.HtmlBody);
    }

    [Fact]
    public void The_link_names_the_requested_language_in_its_query_and_keeps_the_grant_in_its_fragment()
    {
        var message = new SecurityEmail(Guid.NewGuid(), "admitted@example.invalid", "ignored", "grant",
            AuthenticationOperationPurpose.PasswordResetEmail, DateTimeOffset.UtcNow.AddHours(1)) { Language = "ru" };
        var link = "https://dashboard.example.invalid/Identity/ResetPassword?lang=ru#grant=grant&purpose=PasswordResetEmail";
        Assert.Contains(link, SecurityEmailTemplate.Render(message, "https://dashboard.example.invalid/").TextBody);
    }

    // The English sentences each email must not show once it is translated.
    private static readonly string[] EnglishLink = ["ACCOUNT SECURITY", "Hello", "Username", "Use the link below", "This link expires", "If the button does not work", "If you did not request"];
    private static readonly string[] EnglishNotice = ["ACCOUNT SECURITY", "Hello", "Username", "just signed in", "Go to sign in", "If this was not you"];
    private static readonly string[] EnglishMicrosoftOnly = ["ACCOUNT SECURITY", "Hello", "Username", "Someone asked", "Your account signs in", "Go to sign in", "If you did not ask"];

    [Theory]
    [InlineData("ru", "lang=\"ru\"", "Здравствуйте, Name!")]
    [InlineData("ru-RU", "lang=\"ru-RU\"", "Здравствуйте, Name!")]
    [InlineData("ar-IQ", "lang=\"ar-IQ\" dir=\"rtl\"", "مرحبًا Name،")]
    [InlineData("ku-IQ", "lang=\"ku-IQ\" dir=\"rtl\"", "سڵاو Name،")]
    public void Every_email_is_written_in_the_requested_language(string language, string document, string greeting)
    {
        var expires = new DateTimeOffset(2031, 2, 3, 4, 5, 6, TimeSpan.Zero);
        foreach (var purpose in new[] { AuthenticationOperationPurpose.PasswordResetEmail, AuthenticationOperationPurpose.EmailVerify })
        {
            var message = new SecurityEmail(Guid.NewGuid(), "admitted@example.invalid", "ignored", "grant", purpose, expires)
            { FullName = "Name", Username = "username", Language = language };
            AssertWritten(SecurityEmailTemplate.Render(message, "https://dashboard.example.invalid/"), document, greeting, EnglishLink);
            Assert.Contains("2031-02-03 04:05:06 UTC", SecurityEmailTemplate.Render(message, "https://dashboard.example.invalid/").TextBody);
        }
        foreach (var provider in new[] { SignInProvider.Microsoft, SignInProvider.Google })
        {
            var notice = new SecurityEmail(Guid.NewGuid(), "admitted@example.invalid", "ignored", "", AuthenticationOperationPurpose.ProviderLogin, expires)
            { FullName = "Name", Username = "username", ProviderAccount = "account@example.invalid", Provider = provider, Language = language };
            var content = SecurityEmailTemplate.Render(notice, "https://dashboard.example.invalid/");
            AssertWritten(content, document, greeting, EnglishNotice);
            Assert.Contains(provider.ToString(), content.Subject);
            Assert.Contains("account@example.invalid", content.TextBody);
        }
        AssertWritten(SecurityEmailTemplate.Render(new SecurityEmail(Guid.NewGuid(), "admitted@example.invalid", "ignored", "",
            AuthenticationOperationPurpose.PasswordResetEmail, expires) { FullName = "Name", Username = "username", MicrosoftOnly = true, Language = language },
            "https://dashboard.example.invalid/"), document, greeting, EnglishMicrosoftOnly);
    }

    private static void AssertWritten(SecurityEmailContent content, string document, string greeting, string[] english)
    {
        Assert.Contains($"<html {document}>", content.HtmlBody);
        Assert.Contains(greeting, content.TextBody);
        foreach (var body in new[] { content.TextBody, content.HtmlBody, content.Subject })
            foreach (var sentence in english) Assert.DoesNotContain(sentence, body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("uz")]
    [InlineData("not a culture")]
    public void Without_a_translation_the_email_is_in_English_and_says_so(string? language)
    {
        var content = SecurityEmailTemplate.Render(new SecurityEmail(Guid.NewGuid(), "admitted@example.invalid", "ignored", "grant",
            AuthenticationOperationPurpose.PasswordResetEmail, DateTimeOffset.UtcNow.AddHours(1)) { FullName = "Name", Username = "username", Language = language },
            "https://dashboard.example.invalid/");
        Assert.Equal("Reset your password", content.Subject);
        Assert.Contains("Hello Name,", content.TextBody);
        Assert.Contains("Username: username", content.TextBody);
        Assert.Contains("<html lang=\"en\">", content.HtmlBody);
    }

    [Theory]
    [InlineData("ar-IQ", true)]
    [InlineData("ku-IQ", true)]
    [InlineData("ru", false)]
    [InlineData(null, false)]
    public void In_a_right_to_left_email_the_deadline_stays_left_to_right(string? language, bool isolated)
    {
        var content = SecurityEmailTemplate.Render(new SecurityEmail(Guid.NewGuid(), "admitted@example.invalid", "ignored", "grant",
            AuthenticationOperationPurpose.PasswordResetEmail, new DateTimeOffset(2031, 2, 3, 4, 5, 6, TimeSpan.Zero)) { Language = language },
            "https://dashboard.example.invalid/");
        Assert.Equal(isolated, content.TextBody.Contains("⁦2031-02-03 04:05:06 UTC⁩"));
        Assert.Equal(isolated, content.HtmlBody.Contains("⁦2031-02-03 04:05:06 UTC⁩"));
        Assert.Contains("2031-02-03 04:05:06 UTC", content.TextBody);
    }

    [Fact]
    public void The_language_of_the_server_does_not_decide_the_language_of_the_email()
    {
        var culture = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("ru");
        try
        {
            var content = SecurityEmailTemplate.Render(new SecurityEmail(Guid.NewGuid(), "admitted@example.invalid", "ignored", "grant",
                AuthenticationOperationPurpose.PasswordResetEmail, DateTimeOffset.UtcNow.AddHours(1)), "https://dashboard.example.invalid/");
            Assert.Equal("Reset your password", content.Subject);
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = culture; }
    }

    [Theory]
    [InlineData("ru", "ru")]
    [InlineData("ru-RU", "ru-RU")]
    [InlineData("ar-IQ", "ar-IQ")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("r", null)]
    [InlineData("ru\n", null)]
    [InlineData("ru&grant=other", null)]
    [InlineData("ru#x", null)]
    [InlineData("../../evil", null)]
    [InlineData("<script>", null)]
    public void Only_a_well_formed_language_reaches_the_link(string? requested, string? expected) =>
        Assert.Equal(expected, SecurityEmailTemplate.WellFormedLanguage(requested));

    [Fact]
    public void Logo_is_shown_only_when_configured_and_its_URL_is_encoded()
    {
        var message = new SecurityEmail(Guid.NewGuid(), "admitted@example.invalid", "ignored", "grant",
            AuthenticationOperationPurpose.PasswordResetEmail, DateTimeOffset.UtcNow.AddHours(1));
        var logo = SecurityEmailTemplate.Render(message, "https://dashboard.example.invalid/", "https://cdn.example.invalid/logo.png?a=1&b=\"2\"").HtmlBody;
        Assert.Contains("<img src=\"https://cdn.example.invalid/logo.png?a=1&amp;b=&quot;2&quot;\"", logo);
        Assert.DoesNotContain("<img", SecurityEmailTemplate.Render(message, "https://dashboard.example.invalid/").HtmlBody);
    }

    [Fact]
    public void Legacy_template_does_not_guess_expiry_or_generate_a_grant()
    {
        const string url = "https://dashboard.example.invalid/legacy?token=old&user=1";
        var message = SecurityEmailTemplate.RenderLegacy(url, new() { Email = "saved@example.invalid", Username = "saved" }, AuthenticationOperationPurpose.EmailVerify);
        Assert.Contains(url, message.TextBody); Assert.DoesNotContain("expires at", message.TextBody);
        Assert.Contains("If this link has expired", message.HtmlBody);
        Assert.Throws<InvalidOperationException>(() => SecurityEmailTemplate.RenderLegacy("javascript:bad", new(), AuthenticationOperationPurpose.EmailVerify));
    }

}
