using ShiftSoftware.ShiftEntity.Model.Dtos;
using System.ComponentModel.DataAnnotations;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftwareLocalization.Identity;
using FluentValidation;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftIdentity.Core.Localization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.App;

public class AppDTO : ShiftEntityMixedDTO
{
    [JsonHashIdConverter<AppDTO>(5)]
    public override string? ID { get; set; }
    public string DisplayName { get; set; } = default!;

    public string AppId { get; set; } = default!;

    //[MaxLength(255)]
    public string? AppSecret { get; set; }

    public string? Description { get; set; }

    public string RedirectUri { get; set; } = default!;

    //[Required]
    //[MaxLength(4000)]
    //public string PostLogoutRedirectUri { get; set; } = default!;
}

public class AppValidator : AbstractValidator<AppDTO>
{
    public AppValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage(localizer["Please enter a name"])
            .MaximumLength(255).WithMessage(localizer["Your input cannot be more than 255 characters"]);

        RuleFor(x => x.AppId)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["App Id"]])
            .MaximumLength(255).WithMessage(localizer["Your input cannot be more than 255 characters"]);

        RuleFor(x => x.Description)
            .MaximumLength(4000).WithMessage(localizer["Your input cannot be more than 4000 characters"]);

        RuleFor(x => x.RedirectUri)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["Redirect Uri"]])
            .MaximumLength(4000).WithMessage(localizer["Your input cannot be more than 4000 characters"]);

    }
}
