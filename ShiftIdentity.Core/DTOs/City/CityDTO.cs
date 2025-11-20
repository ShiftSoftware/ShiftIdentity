
using FluentValidation;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.City;

public class CityDTO : ShiftEntityViewAndUpsertDTO
{
    [CityHashIdConverter]
    public override string? ID { get; set; }

    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }

    [RegionHashIdConverter]
    public ShiftEntitySelectDTO Region { get; set; } = default!;
    public int? DisplayOrder { get; set; }
}

public class CityValidator : AbstractValidator<CityDTO>
{
    public CityValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(localizer["Please enter a name"])
            .MaximumLength(255).WithMessage(localizer["Your input cannot be more than 255 characters"]);

        RuleFor(x => x.Region)
            .NotNull().WithMessage(localizer["Please select", localizer["Region"]]);
    }
}