using System.Globalization;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftIdentity.Core.DTOs.App;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// The sign-in page appends "/Auth/Token" to an app's RedirectUri, so the App form refuses a new or changed RedirectUri
/// that ends in "/" or in that route (FE-2026-09-25-02). A row that already has one saves unchanged: the app-code
/// binding hashes the stored value, so forcing a fix would end the app's live sessions.
/// </summary>
public class AppValidatorTests
{
    private const string Message = "The Redirect URI cannot end with / or /Auth/Token";

    [Theory]
    [InlineData("https://app.invalid")]
    [InlineData("https://app.invalid/hub")]
    [InlineData("http://localhost:5015")]
    public void A_redirect_uri_without_a_trailing_slash_is_valid(string redirectUri)
    {
        Assert.Empty(Validate(App(redirectUri)).Errors);
    }

    [Theory]
    [InlineData("https://app.invalid/")]
    [InlineData("http://localhost:5015/")]
    [InlineData("https://app.invalid/hub/ ")]
    [InlineData("/")]
    [InlineData("https://app.invalid/Auth/Token")]
    [InlineData("https://app.invalid/auth/token")]
    public void A_new_redirect_uri_ending_in_a_slash_or_the_token_route_is_refused(string redirectUri)
    {
        AssertOnlyMessage(Validate(App(redirectUri)));
    }

    [Fact]
    public void A_row_that_already_ends_in_a_slash_saves_unchanged()
    {
        Assert.Empty(Validate(App("http://localhost:5015/"), registered: "http://localhost:5015/").Errors);
    }

    [Fact]
    public void Changing_a_row_to_a_value_ending_in_a_slash_is_refused()
    {
        AssertOnlyMessage(Validate(App("https://app.invalid/"), registered: "https://app.invalid"));
    }

    [Theory]
    [InlineData("ar-IQ")]
    [InlineData("ku")]
    [InlineData("ru")]
    public void The_message_is_translated(string culture)
    {
        var english = Assert.Single(Validate(App("https://app.invalid/")).Errors);
        var translated = Assert.Single(Validate(App("https://app.invalid/"), culture: CultureInfo.GetCultureInfo(culture)).Errors);
        Assert.NotEqual(english.ErrorMessage, translated.ErrorMessage);
    }

    private static void AssertOnlyMessage(ValidationResult result)
    {
        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(AppDTO.RedirectUri), error.PropertyName);
        Assert.Equal(Message, error.ErrorMessage);
    }

    private static AppDTO App(string redirectUri) => new()
    {
        DisplayName = "Synthetic app", AppId = "synthetic-app", RedirectUri = redirectUri
    };

    // The validator reads its messages when it is built, so the UI culture is set first: the invariant culture
    // (the English resources) unless a test asks for another.
    private static ValidationResult Validate(AppDTO app, string? registered = null, CultureInfo? culture = null)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = culture ?? CultureInfo.InvariantCulture;
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLocalization();
            var localizer = new ShiftIdentityLocalizer(services.BuildServiceProvider(), typeof(ShiftSoftwareLocalization.Identity.Resource));
            return new AppValidator(localizer, () => registered).Validate(app);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
