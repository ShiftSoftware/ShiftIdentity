using FluentValidation;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Brand;

public class BrandDTO : ShiftEntityViewAndUpsertDTO
{
    [BrandHashIdConverter]
    public override string? ID { get; set; }

    public string Name { get; set; } = default!;

    public string? IntegrationId { get; set; } = default!;
}

public class BrandValidator : AbstractValidator<BrandDTO>
{
    public BrandValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["Name"]]);

    }
}
