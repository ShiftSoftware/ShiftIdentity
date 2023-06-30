namespace ShiftSoftware.ShiftIdentity.AspNetCore.Models;

public class TokenSettingsModel
{
    public string Key { get; set; } = default!;

    public string Issuer { get; set; } = default!;

    public string Audience { get; set; } = default!;

    public int ExpireSeconds { get; set; }
}
