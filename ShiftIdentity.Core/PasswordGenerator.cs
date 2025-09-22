using System;
using System.Security.Cryptography;

namespace ShiftSoftware.ShiftIdentity.Core;

public class PasswordGenerator
{
    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digits = "0123456789";
    private const string SpecialChars = "!@#$%^&*()_-+=[]{}|;:,.<>?";

    public static string GeneratePassword(int length)
    {
        if(length <1)
            throw new ArgumentException("Password length must be at least 1.", nameof(length));

        return RandomNumberGenerator.GetString(
            Lowercase + Uppercase + Digits + SpecialChars,
            length
        );
    }
}