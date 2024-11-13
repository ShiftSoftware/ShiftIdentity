using FluentValidation;
using ShiftSoftware.ShiftIdentity.Core.DTOs.App;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.ComponentModel.DataAnnotations;
namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Auth;

public class GenerateAuthCodeDTO
{
    public string AppId { get; set; } = default!;

    public string CodeChallenge { get; set; } = default!;

    public string? ReturnUrl { get; set; }
}

public class GenerateAuthCodeValidator : AbstractValidator<GenerateAuthCodeDTO>
{
    public GenerateAuthCodeValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.AppId)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["App Id"]]);

        RuleFor(x => x.CodeChallenge)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["Code Challenge"]]);

    }
}