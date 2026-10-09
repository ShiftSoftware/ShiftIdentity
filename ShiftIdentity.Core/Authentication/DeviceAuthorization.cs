using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace ShiftSoftware.ShiftIdentity.Core.Authentication;

// Device sign-in (the OAuth 2.0 Device Authorization Grant, RFC 8628). A device with only a remote control asks for a
// pair of codes and shows the public user code as a QR code. A person opens it on a phone, checks that the phone names
// the same code as the device, and types the username and password of the account the device should use. Only an
// account that allows device sign-in can be used. The device polls with its secret device code and receives an
// ordinary session of that account. The phone never signs in.

/// <summary>The state of a device sign-in.</summary>
public enum DeviceAuthorizationState { Pending = 1, Approved = 2, Denied = 3, Consumed = 4, Expired = 5 }

/// <summary>A screen starts a device sign-in. <see cref="ClientId"/> must be one of the host's configured device clients.</summary>
public sealed record StartDeviceAuthorizationRequest(
    [property: Required, MaxLength(64)] string ClientId);

/// <summary>A screen polls with the device code it was given. Only the device code can poll; the user code cannot.</summary>
public sealed record DeviceTokenRequest(
    [property: Required, MaxLength(256)] string DeviceCode);

/// <summary>Denies the code a device shows. Anyone who can see the code may deny it; the device then asks for a new one.</summary>
public sealed record DeviceUserCodeRequest(
    [property: Required, MaxLength(64)] string UserCode);

/// <summary>
/// Signs the device that shows <see cref="UserCode"/> in as the account these credentials prove. The account must allow
/// device sign-in. The password is checked exactly as at login, and a wrong one counts against the account.
/// </summary>
public sealed record ApproveDeviceRequest(
    [property: Required, MaxLength(64)] string UserCode,
    [property: Required, MaxLength(255)] string Username,
    [property: Required, MaxLength(1024)] string Password);

/// <summary>
/// A new device sign-in. The screen keeps <see cref="DeviceCode"/> secret and never puts it in a URL. It shows
/// <see cref="UserCode"/> and a QR code of <see cref="VerificationUriComplete"/>, polls every <see cref="Interval"/>
/// seconds, and asks for a new pair when <see cref="ExpiresIn"/> seconds have passed.
/// </summary>
public sealed record DeviceAuthorizationStarted(string DeviceCode, string UserCode, string VerificationUri,
    string VerificationUriComplete, int ExpiresIn, int Interval) : AuthOutcome;

/// <summary>
/// What the phone shows about a code: which device asks and its state, and once approved, the full name of the account
/// it signs in as (<see cref="AccountName"/> is null before that). It never carries a session.
/// </summary>
public sealed record DeviceAuthorizationView(string UserCode, string ClientDisplayName, string? AccountName,
    DeviceAuthorizationState State, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt) : AuthOutcome;

/// <summary>
/// The user code a screen shows: 8 letters from the alphabet RFC 8628 suggests (no vowels, so no words form, and no
/// digits), shown as <c>XXXX-XXXX</c>. That is about 2^34.6 codes. Input is read without regard to case, dashes or spaces.
/// </summary>
public static class DeviceUserCode
{
    public const string Alphabet = "BCDFGHJKLMNPQRSTVWXZ";
    public const int Length = 8;

    /// <summary>The code in its stored form (8 capital letters), or null when the input cannot be a user code.</summary>
    public static string? Normalize(string? value)
    {
        if (value is null || value.Length > 64) return null;
        var letters = new string(value.Where(c => c != '-' && !char.IsWhiteSpace(c)).Select(char.ToUpperInvariant).ToArray());
        return letters.Length == Length && letters.All(c => Alphabet.IndexOf(c) >= 0) ? letters : null;
    }

    /// <summary>The code as the screen and the phone show it: <c>XXXX-XXXX</c>.</summary>
    public static string Format(string normalized) => normalized.Substring(0, 4) + "-" + normalized.Substring(4);
}
