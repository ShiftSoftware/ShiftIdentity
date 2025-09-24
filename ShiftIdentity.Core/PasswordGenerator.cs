using System;
using System.Security.Cryptography;

namespace ShiftSoftware.ShiftIdentity.Core;

public class PasswordGenerator
{
    private const string Lowercase = "abcdefghjkmnpqrstuvwxyz";
    private const string Uppercase = "ABCDEFGHJKMNPQRSTUVWXYZ";
    private const string Digits = "23456789";
    private const string SpecialChars = "!@#$%^&*()_-+=[]{}|;:,.<>?";

    public static string GeneratePassword(int length)
    {
        if(length <6)
            throw new ArgumentException("Password length must be at least 6.", nameof(length));

        return RandomNumberGenerator.GetString(
            Lowercase + Uppercase + Digits + SpecialChars,
            length
        );
    }
}