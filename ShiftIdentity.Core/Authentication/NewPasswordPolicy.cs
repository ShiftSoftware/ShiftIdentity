using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ShiftSoftware.ShiftIdentity.Core.Authentication;

public enum PasswordPolicyFailure { TooShort, TooLong, InvalidText, Blocked, SameAsCurrent }

/// <summary>Rules for new passwords only. Login continues to verify legacy credentials.</summary>
public sealed class NewPasswordPolicy(IEnumerable<string>? additionalBlockedPasswords = null)
{
    public const int MinimumLength = 15;
    public const int MaximumLength = 128;
    public const string Guidance = "Use 15–128 characters. Spaces and Unicode are allowed. Choose a different password that is not common or based on your username.";
    private readonly HashSet<string> blocked = new(new[]
    {
        "passwordpassword", "passwordpassword1", "passwordpassword123", "password12345678",
        "password123456789", "123456789012345", "1234567890123456", "12345678901234567890",
        "qwertyuiopasdfgh", "qwertyuiopasdfghjkl", "letmeinletmein123", "thisismypassword",
        "iloveyouiloveyou", "correct horse battery staple"
    }.Concat(additionalBlockedPasswords ?? []), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A plain-English sentence for a failure. Callers that localize pass the text through their localizer; the
    /// length failures embed the selected limits so the sentence stays correct if the limits change.
    /// </summary>
    public static string Describe(PasswordPolicyFailure failure) => failure switch
    {
        PasswordPolicyFailure.TooShort => $"The password must be at least {MinimumLength} characters long",
        PasswordPolicyFailure.TooLong => $"The password must not be longer than {MaximumLength} characters",
        PasswordPolicyFailure.InvalidText => "The password contains characters that are not allowed",
        PasswordPolicyFailure.Blocked => "This password is too common or based on the username",
        PasswordPolicyFailure.SameAsCurrent => "New Password can not be the same as the current password",
        _ => Guidance
    };

    public PasswordPolicyFailure? Validate(string? password, string username)
    {
        if (password is null) return PasswordPolicyFailure.TooShort;
        if (password.Length > MaximumLength * 2) return PasswordPolicyFailure.TooLong;
        string normalized;
        try { normalized = password.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return PasswordPolicyFailure.InvalidText; }
        var characters = normalized.EnumerateRunes().ToArray();
        if (characters.Length < MinimumLength) return PasswordPolicyFailure.TooShort;
        if (characters.Length > MaximumLength) return PasswordPolicyFailure.TooLong;
        if (characters.Any(x => Rune.GetUnicodeCategory(x) == UnicodeCategory.Control)) return PasswordPolicyFailure.InvalidText;
        var context = username.Normalize(NormalizationForm.FormC);
        if (blocked.Contains(normalized) || characters.All(x => x == characters[0]) ||
            new[] { context, context + "123", context + "123!", context + "password" }.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            return PasswordPolicyFailure.Blocked;
        return null;
    }
}
