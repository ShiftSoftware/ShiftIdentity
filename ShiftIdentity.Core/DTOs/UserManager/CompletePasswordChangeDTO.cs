using FluentValidation;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.Linq;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;

/// <summary>
/// Payload for finishing the forced-password-change flow. Used after a successful login that
/// returned a challenge token (<see cref="ShiftIdentityClaims.RequirePasswordChange"/>=true).
/// No <c>CurrentPassword</c> — the user just authenticated, so re-asking would be redundant
/// and the challenge token already proves they hold the credentials.
/// </summary>
public class CompletePasswordChangeDTO
{
    public string NewPassword { get; set; } = default!;
    public string ConfirmPassword { get; set; } = default!;
}

public class CompletePasswordChangeValidator : AbstractValidator<CompletePasswordChangeDTO>
{
    public CompletePasswordChangeValidator(ShiftIdentityLocalizer localizer)
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
