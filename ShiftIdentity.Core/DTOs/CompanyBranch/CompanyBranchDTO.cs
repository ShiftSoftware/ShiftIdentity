using FluentValidation;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;

public class CompanyBranchDTO : ShiftEntityViewAndUpsertDTO
{
    [CompanyBranchHashIdConverter]
    public override string? ID { get; set; }

    public string Name { get; set; } = default!;

    [CompanyHashIdConverter]
    public ShiftEntitySelectDTO Company { get; set; } = default!;

    [CompanyHashIdConverter]
    public string? CompanyID { get; set; }

    [CityHashIdConverter]
    public ShiftEntitySelectDTO City { get; set; } = default!;

    public string? Phone { get; set; }
    public string? ShortPhone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? IntegrationId { get; set; } = default!;
    public string? ShortCode { get; set; }

    public string? Latitude { get; set; }
    public string? Longitude { get; set; }
    public List<ShiftFileDTO>? Photos { get; set; }
    public string? WorkingHours { get; set; }
    public string? WorkingDays { get; set; }
    public Dictionary<string, CustomFieldDTO>? CustomFields { get; set; }

    [DepartmentHashIdConverter]
    public List<ShiftEntitySelectDTO> Departments { get; set; } = new List<ShiftEntitySelectDTO>();

    [ServiceHashIdConverter]
    public List<ShiftEntitySelectDTO> Services { get; set; } = new List<ShiftEntitySelectDTO>();

    [BrandHashIdConverter]
    public List<ShiftEntitySelectDTO> Brands { get; set; } = new List<ShiftEntitySelectDTO>();

    public CompanyBranchDTO()
    {
        this.CustomFields = new();
    }
}

public class CompanyBranchValidator : AbstractValidator<CompanyBranchDTO>
{
    public CompanyBranchValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(localizer["Please enter a name"]);

        RuleFor(x=> x.Company)
            .NotNull().WithMessage(localizer["Please select", localizer["Company"]]);

        RuleFor(x => x.City)
            .NotNull().WithMessage(localizer["Please select", localizer["City"]]);

        RuleFor(x => x.Phone)
            .Custom((x, context) =>
            {
                if (x is not null && !ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(x))
                    context.AddFailure(localizer["Invalid Phone Number"]);
            });
    }
}