using FluentValidation;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;

public class ResetPasswordDTO
{
    public string NewPassword { get; set; } = default!;

    public string ConfirmPassword { get; set; } = default!;
}

public class ResetPasswordValidator : AbstractValidator<ResetPasswordDTO>
{
    public ResetPasswordValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["New Password"]])
            .MinimumLength(6).WithMessage(localizer["The password must be at least n characters long", 6])
            .MaximumLength(255).WithMessage(localizer["Your input cannot be more than 255 characters"])
            .Matches(@"^(?=.*[0-9]).+$").WithMessage(localizer["The password must contain at least one digit"])
            .Must(HaveRequiredUniqueChars).WithMessage(localizer["The password must contain at least 3 unique characters"]);

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["Confirm Password"]])
            .Equal(x => x.NewPassword).WithMessage(localizer["New Password and Confirm Password do not match"])
            .MaximumLength(255).WithMessage(localizer["Your input cannot be more than 255 characters"]);

        
    }

    private bool HaveRequiredUniqueChars(string password)
    {
        return password.Distinct().Count() >= 3;
    }
}