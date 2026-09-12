using System.Collections.Generic;

namespace ShiftSoftware.ShiftIdentity.Core.Models;

/// <summary>Shared by all authority instances. Keep old keys until their ciphertext has been replaced or expired.</summary>
public sealed class FactorProtectionSettings
{
    public string ActiveKeyId { get; set; } = "";

    /// <summary>Key identifiers mapped to Base64-encoded, random 32-byte AES keys, supplied by the host.</summary>
    public Dictionary<string, string> Keys { get; set; } = new();
}
