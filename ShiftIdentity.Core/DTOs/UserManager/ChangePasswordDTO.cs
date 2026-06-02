using FluentValidation;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;

public class ChangePasswordDTO
{
    public string NewPassword { get; set; } = default!;

    public string ConfirmPassword { get; set; } = default!;

    public string CurrentPassword { get; set; } = default!;

    /// <summary>
    /// When <c>true</c> (default), the user's security stamp is rolled so every <em>other</em>
    /// session is signed out; the session performing the change stays alive because the caller
    /// re-issues a token carrying the new stamp. Set <c>false</c> to leave the user's other
    /// devices logged in. Only honored for self-service changes — admin resets and forced
    /// (challenge) changes always roll the stamp.
    /// </summary>
    public bool SignOutOtherSessions { get; set; } = true;
}

public class ChangePasswordValidator : AbstractValidator<ChangePasswordDTO>
{
    public ChangePasswordValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["New Password"]])
            .MinimumLength(6).WithMessage(localizer["The password must be at least n characters long", 6])
            .MaximumLength(255).WithMessage(localizer["Your input cannot be more than 255 characters"])
            .Matches(@"^(?=.*[0-9]).+$").WithMessage(localizer["The password must contain at least one digit"])
            .Must(HaveRequiredUniqueChars).WithMessage(localizer["The password must contain at least 3 unique characters"])
            .NotEqual(x => x.CurrentPassword).WithMessage(localizer["The password cannot be the same as the Current Password"]);

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["Confirm Password"]])
            .Equal(x => x.NewPassword).WithMessage(localizer["New Password and Confirm Password do not match"])
            .MaximumLength(255).WithMessage(localizer["Your input cannot be more than 255 characters"]);

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["Current Password"]])
            .MaximumLength(255).WithMessage(localizer["Your input cannot be more than 255 characters"]);
    }

    private bool HaveRequiredUniqueChars(string password)
    {
        return password.Distinct().Count() >= 3;
    }
}