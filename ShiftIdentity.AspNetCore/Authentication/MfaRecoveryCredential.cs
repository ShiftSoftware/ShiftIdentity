using System.Security.Cryptography;
using System.Text;
using OtpNet;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

internal static class MfaRecoveryCredential
{
    internal static (string Code, byte[] Digest) Create(byte[] key)
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        try
        {
            var raw = Base32Encoding.ToString(bytes).TrimEnd('=');
            var code = string.Join('-', Enumerable.Range(0, (raw.Length + 3) / 4).Select(i => raw.Substring(i * 4, Math.Min(4, raw.Length - i * 4))));
            return (code, Digest(raw, key)!);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    internal static bool Verify(string code, byte[]? digest, byte[] key)
    {
        var candidate = Digest(code, key);
        return candidate is not null && digest is { Length: 32 } && CryptographicOperations.FixedTimeEquals(candidate, digest);
    }

    private static byte[]? Digest(string code, byte[] key)
    {
        if (key.Length < 32) throw new CryptographicException("Invalid recovery protection key.");
        if (code.Length > 64) return null;
        var normalized = code.Replace("-", "").Replace(" ", "").ToUpperInvariant();
        if (normalized.Length != 26 || normalized.Any(c => c is not (>= 'A' and <= 'Z') and not (>= '2' and <= '7'))) return null;
        return HMACSHA256.HashData(key, Encoding.ASCII.GetBytes("Identity.MfaRecovery.v2:" + normalized));
    }
}
