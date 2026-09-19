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
    public void Legacy_template_does_not_guess_expiry_or_generate_a_grant()
    {
        const string url = "https://dashboard.example.invalid/legacy?token=old&user=1";
        var message = SecurityEmailTemplate.RenderLegacy(url, new() { Email = "saved@example.invalid", Username = "saved" }, AuthenticationOperationPurpose.EmailVerify);
        Assert.Contains(url, message.TextBody); Assert.DoesNotContain("expires at", message.TextBody);
        Assert.Contains("If this link has expired", message.HtmlBody);
        Assert.Throws<InvalidOperationException>(() => SecurityEmailTemplate.RenderLegacy("javascript:bad", new(), AuthenticationOperationPurpose.EmailVerify));
    }

}
