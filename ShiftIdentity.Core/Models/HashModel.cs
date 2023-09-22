namespace ShiftSoftware.ShiftIdentity.Core.Models;

public class HashModel
{
    public byte[] Salt { get; set; } = default!;

    public byte[] PasswordHash { get; set; } = default!;
}
