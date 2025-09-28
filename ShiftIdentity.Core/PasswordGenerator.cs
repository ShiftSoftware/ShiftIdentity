using System;
using System.Linq;
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
        if (length < 6)
            throw new ArgumentException("Password length must be at least 6.", nameof(length));
        if (length > 255)
            throw new ArgumentException("Password length must not exceed 255.", nameof(length));

        var allChars = Lowercase + Uppercase + Digits + SpecialChars;
        var passwordChars = new char[length];

        // Ensure at least one digit
        passwordChars[0] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];

        // Fill the rest randomly
        for (int i = 1; i < length; i++)
        {
            passwordChars[i] = allChars[RandomNumberGenerator.GetInt32(allChars.Length)];
        }

        // Shuffle to avoid digit always at the start
        passwordChars = passwordChars.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).ToArray();

        // Ensure at least 3 unique characters
        int uniqueCount = passwordChars.Distinct().Count();
        if (uniqueCount < 3)
        {
            // Replace random positions with unique characters until the requirement is met
            var uniquePool = allChars.Except(passwordChars).ToList();
            int idx = 0;
            while (uniqueCount < 3 && uniquePool.Count > 0)
            {
                passwordChars[idx % length] = uniquePool[0];
                uniquePool.RemoveAt(0);
                uniqueCount = passwordChars.Distinct().Count();
                idx++;
            }
        }

        return new string(passwordChars);
    }
}