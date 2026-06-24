using FluentValidation;
using ShiftSoftware.ShiftIdentity.Core.Localization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs;

/// <summary>
/// Payload for the MFA login-verification step. Method-agnostic for code-based factors (TOTP, SMS,
/// email OTP) — the server dispatches to whichever method the user has enrolled.
/// </summary>
public class MfaDTO
{
    public string Code { get; set; } = default!;
}

public class MfaValidator : AbstractValidator<MfaDTO>
{
    public MfaValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["Two Factor Code"]]);
    }
}
