using ShiftSoftware.ShiftIdentity.Core.Authentication;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public static class SecurityLinkNavigation
{
    public static bool TryRead(string uri, bool verification, out string grant, out AuthenticationOperationPurpose purpose)
    {
        grant = ""; purpose = default;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var address) || address.Fragment.Length > 1024) return false;
        try
        {
            var parts = address.Fragment.TrimStart('#').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2)).ToArray();
            if (parts.Length != 2 || parts.Any(part => part.Length != 2) ||
                parts.Count(part => part[0] == "grant") != 1 || parts.Count(part => part[0] == "purpose") != 1) return false;
            grant = Uri.UnescapeDataString(parts.Single(part => part[0] == "grant")[1]);
            var name = Uri.UnescapeDataString(parts.Single(part => part[0] == "purpose")[1]);
            if (!Enum.TryParse(name, out purpose) || purpose.ToString() != name || grant.Length is 0 or > 512 || grant.Any(char.IsControl)) return false;
            return verification ? purpose == AuthenticationOperationPurpose.EmailVerify
                : purpose is AuthenticationOperationPurpose.PasswordResetEmail or AuthenticationOperationPurpose.PasswordResetManual;
        }
        catch (UriFormatException) { return false; }
    }

    public static string ResetLink(string grant) => "Identity/ResetPassword#grant=" + Uri.EscapeDataString(grant) + "&purpose=PasswordResetManual";
    public const string LoginAfterReset = "Identity/login?securityLink=reset";
    public const string LoginAfterVerification = "Identity/login?securityLink=verification";
    public const string LoginAfterCancel = "Identity/login?securityLink=cancelled";
}
