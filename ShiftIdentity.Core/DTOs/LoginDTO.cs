using FluentValidation;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs;

public class LoginDTO
{
    public string Username { get; set; } = default!;

    public string Password { get; set; } = default!;
}

public class LoginValidator : AbstractValidator<LoginDTO> 
{
    public LoginValidator(ShiftIdentityLocalizer localizer)
    {

        RuleFor(x => x.Username)
                .NotEmpty().WithMessage(localizer["Please provide", localizer["Username"]])
                .MaximumLength(255).WithMessage(localizer["Your input cannot be more than 255 characters"]);

        RuleFor(x => x.Password)
                .NotEmpty().WithMessage(localizer["Please provide", localizer["Password"]])
                .MaximumLength(255).WithMessage(localizer["Your input cannot be more than 255 characters"]);
    }
}