using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using ShiftSoftware.ShiftIdentity.Core.Models;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>Configured AES-GCM protection. The authenticated envelope carries its format and key identifier.</summary>
internal sealed class IdentityMaterialProtector
{
    private readonly string activeKeyId;
    private readonly IReadOnlyDictionary<string, byte[]> keys;
    private readonly string[] purposes;
    private const int NonceLength = 12, TagLength = 16;

    internal IdentityMaterialProtector(FactorProtectionSettings settings)
    {
        if (settings is null || !ValidId(settings.ActiveKeyId) || settings.Keys is null ||
            !settings.Keys.Keys.Contains(settings.ActiveKeyId, StringComparer.Ordinal))
            throw new InvalidOperationException("Identity factor protection requires an active configured key.");
        var loaded = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in settings.Keys)
        {
            if (!ValidId(entry.Key)) throw new InvalidOperationException("Invalid identity factor key identifier.");
            byte[] key;
            try { key = Convert.FromBase64String(entry.Value); }
            catch (Exception e) when (e is FormatException or ArgumentNullException)
            { throw new InvalidOperationException("Identity factor keys must be Base64-encoded 32-byte keys."); }
            if (key.Length != 32) throw new InvalidOperationException("Identity factor keys must contain 32 bytes.");
            loaded.Add(entry.Key, key);
        }
        activeKeyId = settings.ActiveKeyId;
        keys = loaded;
        purposes = ["ShiftIdentity.Material.v1"];
    }

    private IdentityMaterialProtector(IdentityMaterialProtector parent, string purpose)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        activeKeyId = parent.activeKeyId;
        keys = parent.keys;
        purposes = [.. parent.purposes, purpose];
    }

    internal IdentityMaterialProtector CreateProtector(string purpose) => new(this, purpose);

    internal byte[] Protect(byte[] plaintext)
    {
        var id = Encoding.ASCII.GetBytes(activeKeyId);
        var headerLength = 5 + id.Length;
        var result = new byte[headerLength + NonceLength + TagLength + plaintext.Length];
        "SIM1"u8.CopyTo(result);
        result[4] = (byte)id.Length;
        id.CopyTo(result, 5);
        var nonce = result.AsSpan(headerLength, NonceLength);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(keys[activeKeyId], TagLength);
        aes.Encrypt(nonce, plaintext, result.AsSpan(headerLength + NonceLength + TagLength),
            result.AsSpan(headerLength + NonceLength, TagLength), AssociatedData(result.AsSpan(0, headerLength)));
        return result;
    }

    internal byte[] Unprotect(byte[] ciphertext)
    {
        if (ciphertext.Length < 5 + 1 + NonceLength + TagLength || !ciphertext.AsSpan(0, 4).SequenceEqual("SIM1"u8) ||
            ciphertext[4] is < 1 or > 64)
            throw new CryptographicException("Invalid identity material envelope.");
        var headerLength = 5 + ciphertext[4];
        if (ciphertext.Length < headerLength + NonceLength + TagLength ||
            !keys.TryGetValue(Encoding.ASCII.GetString(ciphertext, 5, ciphertext[4]), out var key))
            throw new CryptographicException("Identity material key is unavailable or the envelope is invalid.");
        var plaintext = new byte[ciphertext.Length - headerLength - NonceLength - TagLength];
        using var aes = new AesGcm(key, TagLength);
        try
        {
            aes.Decrypt(ciphertext.AsSpan(headerLength, NonceLength),
                ciphertext.AsSpan(headerLength + NonceLength + TagLength),
                ciphertext.AsSpan(headerLength + NonceLength, TagLength), plaintext,
                AssociatedData(ciphertext.AsSpan(0, headerLength)));
            return plaintext;
        }
        catch { CryptographicOperations.ZeroMemory(plaintext); throw; }
    }

    internal string Protect(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        try { return WebEncoders.Base64UrlEncode(Protect(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    internal string Unprotect(string ciphertext)
    {
        var bytes = Unprotect(WebEncoders.Base64UrlDecode(ciphertext));
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private byte[] AssociatedData(ReadOnlySpan<byte> header)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        // Length-prefixed purposes cannot be confused with a different nesting of the same text.
        writer.Write(purposes.Length);
        foreach (var purpose in purposes) writer.Write(purpose);
        writer.Write(header);
        return stream.ToArray();
    }

    private static bool ValidId(string? id) => id is { Length: >= 1 and <= 64 } &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
