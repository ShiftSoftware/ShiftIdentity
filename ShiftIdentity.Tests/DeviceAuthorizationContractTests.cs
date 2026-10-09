using System.Text.Json;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>The wire contract of device sign-in and the user code's reading rules. No database is involved here.</summary>
[Trait("Category", "Policy")]
public sealed class DeviceAuthorizationContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("WDJB-MJHT", "WDJBMJHT")]
    [InlineData("wdjbmjht", "WDJBMJHT")]
    [InlineData(" wdjb - mjht ", "WDJBMJHT")]
    [InlineData("WDJB-MJH", null)]
    [InlineData("WDJB-MJHTX", null)]
    [InlineData("WDJA-MJHT", null)]
    [InlineData("WDJB-MJH1", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void A_user_code_is_read_without_regard_to_case_dashes_or_spaces(string? typed, string? expected)
    {
        Assert.Equal(expected, DeviceUserCode.Normalize(typed));
        if (expected is not null) Assert.Equal("WDJB-MJHT", DeviceUserCode.Format(expected));
    }

    [Fact]
    public void The_user_code_alphabet_has_no_vowels_or_digits_and_gives_about_two_to_the_34_codes()
    {
        Assert.Equal(20, DeviceUserCode.Alphabet.Distinct().Count());
        Assert.DoesNotContain(DeviceUserCode.Alphabet, c => "AEIOUY".Contains(c) || char.IsDigit(c));
        Assert.True(Math.Pow(DeviceUserCode.Alphabet.Length, DeviceUserCode.Length) > Math.Pow(2, 34));
    }

    [Theory]
    [InlineData(AuthenticationFailure.AuthorizationPending, "authorization_pending")]
    [InlineData(AuthenticationFailure.SlowDown, "slow_down")]
    [InlineData(AuthenticationFailure.AttemptsExhausted, "slow_down")]
    [InlineData(AuthenticationFailure.AccessDenied, "access_denied")]
    [InlineData(AuthenticationFailure.AccountUnavailable, "access_denied")]
    [InlineData(AuthenticationFailure.StaleOperation, "access_denied")]
    [InlineData(AuthenticationFailure.ClientDenied, "access_denied")]
    [InlineData(AuthenticationFailure.DeviceSignInNotAllowed, "access_denied")]
    [InlineData(AuthenticationFailure.ExpiredToken, "expired_token")]
    [InlineData(AuthenticationFailure.InvalidGrant, "invalid_grant")]
    [InlineData(AuthenticationFailure.InvalidRequest, "invalid_request")]
    [InlineData(AuthenticationFailure.Unavailable, "temporarily_unavailable")]
    public void A_poll_refusal_carries_its_oauth_error_code(AuthenticationFailure code, string error)
    {
        var json = JsonSerializer.Serialize<AuthOutcome>(AuthService.DeviceRefusal(code), Web);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("refused", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal(error, document.RootElement.GetProperty("error").GetString());
        Assert.Equal(code, Assert.IsType<AuthenticationRefused>(JsonSerializer.Deserialize<AuthOutcome>(json, Web)).Code);
    }

    [Fact]
    public void Other_refusals_keep_their_wire_shape_without_an_error_property()
    {
        var json = JsonSerializer.Serialize<AuthOutcome>(new AuthenticationRefused(AuthenticationFailure.InvalidGrant), Web);
        Assert.Equal("""{"kind":"refused","code":2,"passwordFailure":null}""", json);
    }

    [Fact]
    public void The_device_outcomes_round_trip_with_their_own_kinds()
    {
        var started = new DeviceAuthorizationStarted("device", "WDJB-MJHT", "https://identity.invalid/Identity/device",
            "https://identity.invalid/Identity/device?code=WDJB-MJHT", 600, 5);
        var startedJson = JsonSerializer.Serialize<AuthOutcome>(started, Web);
        Assert.StartsWith("""{"kind":"deviceAuthorizationStarted","deviceCode":"device","userCode":"WDJB-MJHT",""", startedJson);
        Assert.Equal(started, JsonSerializer.Deserialize<AuthOutcome>(startedJson, Web));
        var view = new DeviceAuthorizationView("WDJB-MJHT", "Service Screen", "Synthetic User", DeviceAuthorizationState.Approved,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(10));
        var viewJson = JsonSerializer.Serialize<AuthOutcome>(view, Web);
        Assert.Contains("\"kind\":\"deviceAuthorization\"", viewJson);
        Assert.Equal(view, JsonSerializer.Deserialize<AuthOutcome>(viewJson, Web));
        // Before approval the phone's view names no account.
        var pending = view with { AccountName = null, State = DeviceAuthorizationState.Pending };
        Assert.Equal(pending, JsonSerializer.Deserialize<AuthOutcome>(JsonSerializer.Serialize<AuthOutcome>(pending, Web), Web));
    }

    [Fact]
    public void The_approval_carries_the_code_and_the_account_credentials_and_its_refusal_is_appended()
    {
        Assert.Equal("""{"userCode":"WDJB-MJHT","username":"screen","password":"1"}""",
            JsonSerializer.Serialize(new ApproveDeviceRequest("WDJB-MJHT", "screen", "1"), Web));
        // Appended after the RFC 8628 codes, so every earlier code keeps its number on the wire.
        Assert.Equal(17, (int)AuthenticationFailure.ExpiredToken);
        Assert.Equal(18, (int)AuthenticationFailure.DeviceSignInNotAllowed);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void A_password_change_request_is_refused_only_for_an_account_that_allows_device_sign_in(bool allowsDeviceSignIn, bool requireChange, bool refused)
    {
        var refusal = AccountSecurityService.ChangeRequestRefusal(allowsDeviceSignIn, requireChange);
        Assert.Equal(refused, refusal is not null);
        if (refusal is null) return;
        Assert.Equal(AuthenticationFailure.RequiredPasswordChangeNotAllowed, refusal.Code);
        // Appended after the device sign-in refusal, so every earlier code keeps its number on the wire.
        Assert.Equal("""{"kind":"refused","code":19,"passwordFailure":null}""", JsonSerializer.Serialize<AuthOutcome>(refusal, Web));
    }
}
