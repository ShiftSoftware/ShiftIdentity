using FluentValidation;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Service;

public class ServiceDTO : ShiftEntityViewAndUpsertDTO
{
    [ServiceHashIdConverter]
    public override string? ID { get; set; }

    public string Name { get; set; } = default!;

    public string? IntegrationId { get; set; }
}

public class ServiceValidator : AbstractValidator<ServiceDTO>
{
    public ServiceValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(localizer["Please enter a name"]);
    }
}