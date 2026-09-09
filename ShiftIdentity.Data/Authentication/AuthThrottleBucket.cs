namespace ShiftSoftware.ShiftIdentity.Data.Authentication;

/// <summary>A durable request budget shared across hosts. The key is a digest, never a raw address.</summary>
public sealed class AuthThrottleBucket
{
    public string Key { get; set; } = "";
    public DateTimeOffset WindowStart { get; set; }
    public int Count { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
