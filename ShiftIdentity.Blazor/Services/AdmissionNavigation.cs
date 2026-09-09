namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public static class AdmissionNavigation
{
    public static string LocalReturnPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\\') || value.Any(char.IsControl) ||
            value.StartsWith("//", StringComparison.Ordinal) ||
            (!value.StartsWith('/') && Uri.TryCreate(value, UriKind.Absolute, out _))) return "/";
        return "/" + value.TrimStart('/');
    }
}
