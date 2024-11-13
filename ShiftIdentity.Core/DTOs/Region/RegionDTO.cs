
using FluentValidation;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Region;

public class RegionDTO : ShiftEntityViewAndUpsertDTO
{
    [RegionHashIdConverter]
    public override string? ID { get; set; }

    [Required]
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
    public string? ShortCode { get; set; }

    [Required]
    [CountryHashIdConverter]
    public ShiftEntitySelectDTO Country { get; set; } = default!;
}

public class RegionValidator : AbstractValidator<RegionDTO>
{
    public RegionValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(localizer["Please enter a name"]);

        RuleFor(x => x.Country)
            .NotNull().WithMessage(localizer["Please select", localizer["Country"]]);
    }
}