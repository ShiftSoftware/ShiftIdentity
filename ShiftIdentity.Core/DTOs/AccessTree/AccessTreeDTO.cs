using ShiftSoftware.ShiftEntity.Model.Dtos;
using System.ComponentModel.DataAnnotations;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftEntity.Model;
using FluentValidation;
using ShiftSoftware.ShiftIdentity.Core.Localization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;

[ShiftEntityKeyAndName(nameof(ID), nameof(Name))]
public class AccessTreeDTO : ShiftEntityMixedDTO
{
    [JsonHashIdConverter<AccessTreeDTO>(5)]
    public override string? ID { get; set; }
    public string Name { get; set; } = default!;

    public string Tree { get; set; } = default!;
}

public class AccessTreeValidator : AbstractValidator<AccessTreeDTO>
{
    public AccessTreeValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(localizer["Please enter a name"])
            .MaximumLength(255).WithMessage("Input must be 255 characters or less");

        RuleFor(x => x.Tree)
            .NotEmpty().WithMessage(localizer["Please select an action from the options"]);
    }
}
