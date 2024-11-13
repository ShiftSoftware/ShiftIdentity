using System.ComponentModel.DataAnnotations;
using System;
using FluentValidation;
using ShiftSoftware.ShiftIdentity.Core.Localization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs;


public class GenerateExternalTokenWithAppIdOnlyDTO
{
    public Guid AuthCode { get; set; }

    public string AppId { get; set; } = default!;

    public string CodeVerifier { get; set; } = default!;
}

public class GenerateExternalTokenWithAppIdOnlyValidator : AbstractValidator<GenerateExternalTokenWithAppIdOnlyDTO>
{
    public GenerateExternalTokenWithAppIdOnlyValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.AuthCode)
            .NotNull().WithMessage(localizer["Please provide", localizer["Auth Code"]]);

        RuleFor(x => x.AppId)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["App Id"]]);

        RuleFor(x => x.CodeVerifier)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["Code Verifier"]]);
    }
}