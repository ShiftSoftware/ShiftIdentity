using FluentValidation;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Team;

public class TeamDTO : ShiftEntityViewAndUpsertDTO
{
    [TeamHashIdConverter]
    public override string? ID { get; set; }

    public string Name { get; set; } = default!;

    [CompanyHashIdConverter]
    public ShiftEntitySelectDTO Company { get; set; } = default!;

    public string? IntegrationId { get; set; }

    [UserHashIdConverter]
    public IEnumerable<ShiftEntitySelectDTO> Users { get; set; } = new List<ShiftEntitySelectDTO>();
}

public class TeamValidator : AbstractValidator<TeamDTO>
{
    public TeamValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(localizer["Please enter a name"]);

        RuleFor(x => x.Company)
            .NotNull().WithMessage(localizer["Please select", localizer["Company"]]);
    }
}