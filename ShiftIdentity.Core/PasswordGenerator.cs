using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core;

public class PasswordGenerator
{
    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digits = "0123456789";
    private const string SpecialChars = "!@#$%^&*()_-+=[]{}|;:,.<>?";

    public static string GeneratePassword(int length)
    {
        string allChars = Lowercase + Uppercase + Digits + SpecialChars;
        Random random = new Random();
        return new string(Enumerable.Repeat(allChars, length)
          .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    public static IEnumerable<string> GeneratePasswords(int count, int length)
    {
        return Enumerable.Range(0, count).Select(i => GeneratePassword(length));
    }
}
