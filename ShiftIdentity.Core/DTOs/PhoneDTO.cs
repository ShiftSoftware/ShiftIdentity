using FluentValidation;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs;


public class PhoneDTO
{
    public string Phone { get; set; } = default!;

    public bool IsVerified { get; set; }
}

public class PhoneValidator : AbstractValidator<PhoneDTO>
{
    public PhoneValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["Phone"]])
            .Custom((x, context) =>
            {
                if (x is not null && !ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(x))
                    context.AddFailure(localizer["Invalid Phone Number"]);
            });
    }
}