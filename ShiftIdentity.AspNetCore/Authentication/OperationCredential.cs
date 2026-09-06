using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

internal static class OperationCredential
{
    public static (Guid ID, string Handle, byte[] Digest) Create(byte[] key)
    {
        var id = Guid.NewGuid();
        var secret = RandomNumberGenerator.GetBytes(32);
        return (id, $"{id:N}.{WebEncoders.Base64UrlEncode(secret)}", Digest(key, id, secret));
    }

    public static bool TryRead(string? handle, byte[] key, out Guid id, out byte[] digest)
    {
        id = default;
        digest = [];
        if (handle is null || handle.Length != 76 || handle[32] != '.' ||
            !Guid.TryParseExact(handle[..32], "N", out id)) return false;
        try
        {
            var secret = WebEncoders.Base64UrlDecode(handle[33..]);
            if (secret.Length != 32 || WebEncoders.Base64UrlEncode(secret) != handle[33..]) return false;
            digest = Digest(key, id, secret);
            return true;
        }
        catch (FormatException) { return false; }
    }

    public static bool IsChallenge(string? challenge)
    {
        if (challenge is null || challenge.Length != 43) return false;
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(challenge);
            return bytes.Length == 32 && WebEncoders.Base64UrlEncode(bytes) == challenge;
        }
        catch (FormatException) { return false; }
    }

    public static bool VerifyChallenge(string challenge, string verifier)
    {
        if (verifier.Length is < 43 or > 128 ||
            verifier.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.' and not '~')) return false;
        var actual = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(challenge));
    }

    private static byte[] Digest(byte[] key, Guid id, byte[] secret) =>
        HMACSHA256.HashData(key, Encoding.ASCII.GetBytes($"{id:N}.{WebEncoders.Base64UrlEncode(secret)}"));
}
