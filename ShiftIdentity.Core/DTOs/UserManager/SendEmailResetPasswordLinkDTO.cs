using FluentValidation;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;

public class SendEmailResetPasswordLinkDTO
{
    public string? Email { get; set; } = default!;
}

public class SendEmailResetPasswordLinkValidator : AbstractValidator<SendEmailResetPasswordLinkDTO>
{
    public SendEmailResetPasswordLinkValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["Email"]])
            .EmailAddress().WithMessage(localizer["Invalid Email Address"])
            .MaximumLength(255).WithMessage(localizer["Your input cannot be more than 255 characters"]);
    }
}