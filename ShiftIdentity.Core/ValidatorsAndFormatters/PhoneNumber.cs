using System.Runtime.CompilerServices;

namespace ShiftSoftware.ShiftIdentity.Core.ValidatorsAndFormatters;

public static class PhoneNumber
{
    // The browser skips the check until the lazy PhoneNumbers assembly is loaded; the server validates every save.
    public static bool EngineAvailable { get; set; } = true;

    public static string GetFormattedPhone(string phone)
    {
        var phoneNumberUtil = PhoneNumbers.PhoneNumberUtil.GetInstance();

        var phoneNumber = phoneNumberUtil.Parse(phone, "IQ");

        return phoneNumberUtil.Format(phoneNumber, PhoneNumbers.PhoneNumberFormat.INTERNATIONAL);
    }

    public static bool PhoneIsValid(string phone)
    {
        if (!EngineAvailable)
            return true;

        return ParseAndValidate(phone);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ParseAndValidate(string phone)
    {
        try
        {
            var phoneNumberUtil = PhoneNumbers.PhoneNumberUtil.GetInstance();

            var phoneNumber = phoneNumberUtil.Parse(phone, "IQ");

            return phoneNumberUtil.IsValidNumber(phoneNumber);
        }
        catch
        {
            return false;
        }
    }
}
