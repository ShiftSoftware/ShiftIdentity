using FluentValidation;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs;


public class EmailDTO
{
    public string Email { get; set; } = default!;

    public bool IsVerified { get; set; }
}

public class EmailValidator: AbstractValidator<EmailDTO>
{
    public EmailValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["Email"]])
            .EmailAddress().WithMessage(localizer["Invalid Email Address"]);
    }
}