using FluentValidation;
using ShiftSoftware.ShiftIdentity.Core.Localization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs;


public class RefreshDTO
{
    public string RefreshToken { get; set; } = default!;
}

public class RefreshValidator : AbstractValidator<RefreshDTO>
{
    public RefreshValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["Refresh Token"]]);
    }
}