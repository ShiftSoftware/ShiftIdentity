using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class NotEqualToAttribute : ValidationAttribute
{
    private readonly string _otherPropertyName;

    public NotEqualToAttribute(string otherPropertyName)
    {
        _otherPropertyName = otherPropertyName;
    }

    protected override ValidationResult IsValid(object value, ValidationContext validationContext)
    {
        var currentValue = value;
        var otherProperty = validationContext.ObjectType.GetProperty(_otherPropertyName);
        if (otherProperty == null)
            throw new ArgumentException($"Property '{_otherPropertyName}' not found.");

        var otherValue = otherProperty.GetValue(validationContext.ObjectInstance);
        if (Equals(currentValue, otherValue))
            return new ValidationResult(ErrorMessage);

        return ValidationResult.Success;
    }
}
