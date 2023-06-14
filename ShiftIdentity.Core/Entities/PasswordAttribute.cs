using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

public class PasswordAttribute : ValidationAttribute
{
    public int RequiredLength { get; set; }
    public int RequiredUniqueChars { get; set; }
    public bool RequireDigit { get; set; }
    public bool RequireLowercase { get; set; }
    public bool RequireNonAlphanumeric { get; set; }
    public bool RequireUppercase { get; set; }

    public PasswordAttribute(
        int requiredLength = 1,
        int requiredUniqueChars = 1,
        bool requireDigit = false,
        bool requireLowercase = false,
        bool requireNonAlphanumeric = false,
        bool requireUppercase = false)
    {
        RequiredLength = requiredLength;
        RequiredUniqueChars = requiredUniqueChars;
        RequireDigit = requireDigit;
        RequireLowercase = requireLowercase;
        RequireNonAlphanumeric = requireNonAlphanumeric;
        RequireUppercase = requireUppercase;
    }

    public override bool IsValid(object value)
    {
        if (value == null)
        {
            return true; // Don't validate null values
        }

        var password = value.ToString();

        if (password.Length < RequiredLength)
        {
            ErrorMessage = $"The password must be at least {RequiredLength} characters long.";
            return false;
        }

        if (RequiredUniqueChars > password.Distinct().Count())
        {
            ErrorMessage = $"The password must contain at least {RequiredUniqueChars} unique characters.";
            return false;
        }

        if (RequireDigit && !password.Any(char.IsDigit))
        {
            ErrorMessage = "The password must contain at least one digit.";
            return false;
        }

        if (RequireLowercase && !password.Any(char.IsLower))
        {
            ErrorMessage = "The password must contain at least one lowercase letter.";
            return false;
        }

        if (RequireNonAlphanumeric && !password.Any(char.IsSymbol))
        {
            ErrorMessage = "The password must contain at least one non-alphanumeric character.";
            return false;
        }

        if (RequireUppercase && !password.Any(char.IsUpper))
        {
            ErrorMessage = "The password must contain at least one uppercase letter.";
            return false;
        }

        return true;
    }
}
