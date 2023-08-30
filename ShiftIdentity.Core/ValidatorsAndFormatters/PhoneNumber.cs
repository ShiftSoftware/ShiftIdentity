namespace ShiftSoftware.ShiftIdentity.Core.ValidatorsAndFormatters;

public static class PhoneNumber
{
    public static string GetFormattedPhone(string phone)
    {
        var phoneNumberUtil = PhoneNumbers.PhoneNumberUtil.GetInstance();

        var phoneNumber = phoneNumberUtil.Parse(phone, "IQ");

        return phoneNumberUtil.Format(phoneNumber, PhoneNumbers.PhoneNumberFormat.INTERNATIONAL);
    }

    public static bool PhoneIsValid(string phone)
    {
        var phoneNumberUtil = PhoneNumbers.PhoneNumberUtil.GetInstance();

        var phoneNumber = phoneNumberUtil.Parse(phone, "IQ");

        return phoneNumberUtil.IsValidNumber(phoneNumber);
    }
}
