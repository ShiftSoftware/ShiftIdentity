using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ShiftSoftware.ShiftIdentity.Core.Models;

namespace ShiftSoftware.ShiftIdentity.Core.Authentication;

/// <summary>Version 1: NFC UTF-8, PBKDF2-HMAC-SHA256, a 128-bit salt and a 256-bit subkey.</summary>
public static class VersionedPasswordHash
{
    public const int Iterations = 600_000;
    private static ReadOnlySpan<byte> Marker => "SI2P"u8;

    public static HashModel Create(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = new byte[41];
        Marker.CopyTo(hash);
        hash[4] = 1;
        BinaryPrimitives.WriteInt32BigEndian(hash.AsSpan(5, 4), Iterations);
        var derived = Derive(password, salt, Iterations);
        try { derived.CopyTo(hash, 9); }
        finally { CryptographicOperations.ZeroMemory(derived); }
        return new() { Salt = salt, PasswordHash = hash };
    }

    public static bool NeedsUpgrade(byte[] hash) => hash.Length == 64 ||
        (IsVersioned(hash) && BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(5, 4)) < Iterations);

    public static bool Verify(string password, byte[] salt, byte[] hash)
    {
        if (password is null || password.Length > 1024 || salt is null || hash is null) return false;
        if (!IsVersioned(hash))
        {
            // Legacy verification is exact UTF-8. A malformed versioned value never falls back.
            if (hash.Length != 64 || salt.Length is < 1 or > 128 || hash.AsSpan().StartsWith(Marker)) return false;
            var bytes = Encoding.UTF8.GetBytes(password);
            var legacy = HMACSHA512.HashData(salt, bytes);
            try { return CryptographicOperations.FixedTimeEquals(legacy, hash); }
            finally { CryptographicOperations.ZeroMemory(bytes); CryptographicOperations.ZeroMemory(legacy); }
        }
        var iterations = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(5, 4));
        if (salt.Length != 16 || iterations is < 10_000 or > 2_000_000) return false;
        try
        {
            var actual = Derive(password, salt, iterations);
            try { return CryptographicOperations.FixedTimeEquals(actual, hash.AsSpan(9)); }
            finally { CryptographicOperations.ZeroMemory(actual); }
        }
        catch (ArgumentException) { return false; }
    }

    private static bool IsVersioned(byte[] hash) => hash.Length == 41 && hash.AsSpan(0, 4).SequenceEqual(Marker) && hash[4] == 1;
    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password.Normalize(NormalizationForm.FormC), salt, iterations, HashAlgorithmName.SHA256, 32);
}
