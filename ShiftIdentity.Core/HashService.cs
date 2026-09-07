
using ShiftSoftware.ShiftIdentity.Core.Models;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ShiftSoftware.ShiftIdentity.Core;

public class HashService
{
    // Opt-in staged writers use the adaptive format. Existing production writers are adapted
    // together at authority cutover; these entry points do not change their registration.
    public static HashModel GenerateVersionedHash(string password) => Authentication.VersionedPasswordHash.Create(password);
    public static bool VerifyVersionedPassword(string password, byte[] salt, byte[] passwordHash) =>
        Authentication.VersionedPasswordHash.Verify(password, salt, passwordHash);

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
        using (var sha512 = SHA512.Create())
        {
            var hash = sha512.ComputeHash(Encoding.UTF8.GetBytes(text));
            var hashString = Convert.ToHexString(hash);

            return hashString;
        }
    }

    public static bool SHA512Verify(string text, string textHash)
    {
        return textHash == SHA512GenerateHash(text);
    }
}
