namespace ShiftSoftware.ShiftIdentity.AspNetCore;

public class HashIdSettings
{
    public bool AcceptUnencodedIds { get; set; }
    public string? UserIdsSalt { get; set; }
    public int UserIdsMinHashLength { get; set; }
    public string? UserIdsAlphabet { get; set; }
}
