
using FluentValidation;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Department;

public class DepartmentDTO : ShiftEntityViewAndUpsertDTO
{
    [DepartmentHashIdConverter]
    public override string? ID { get; set; }

    public string Name { get; set; } = default!;

    public string? IntegrationId { get; set; }
}

public class DepartmentValidator : AbstractValidator<DepartmentDTO>
{
    public DepartmentValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(localizer["Please enter a name"]);
    }
}
