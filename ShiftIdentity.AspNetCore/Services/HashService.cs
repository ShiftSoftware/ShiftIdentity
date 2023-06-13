using ShiftSoftware.ShiftIdentity.Core.Models;
using System.Security.Cryptography;
using System.Text;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

public class HashService
{
    public static HashModel GenerateHash(string password)
    {
        HashModel result = new HashModel();

        using (var hmac = new HMACSHA512())
        {
            result.Salt = hmac.Key;
            result.PasswordHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
        }

        return result;
    }

    public static bool VerifyPassword(string password, byte[] salt, byte[] passwordHash)
    {
        using (var hmac = new HMACSHA512(salt))
        {
            var generatedPassowrdHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));

            return generatedPassowrdHash.SequenceEqual(passwordHash);
        }
    }

    public static string SHA512GenerateHash(string text)
    {
        var sha512 = SHA512.Create();

        var hash = sha512.ComputeHash(Encoding.UTF8.GetBytes(text));
        var hashString = Convert.ToHexString(hash);

        return hashString;
    }

    public static bool SHA512Verify(string text, string textHash)
    {
        return textHash == SHA512GenerateHash(text);
    }
}
