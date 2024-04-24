using FluentValidation;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;

public class CompanyBranchDTO : ShiftEntityViewAndUpsertDTO
{
    [CompanyBranchHashIdConverter]
    public override string? ID { get; set; }

    [Required]
    public string Name { get; set; } = default!;

    [Required]
    [CompanyHashIdConverter]
    public ShiftEntitySelectDTO Company { get; set; } = default!;

    [CompanyHashIdConverter]
    public string? CompanyID { get; set; }

    [Required]
    [CityHashIdConverter]
    public ShiftEntitySelectDTO City { get; set; } = default!;

    public string? Phone { get; set; }
    public string? ShortPhone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? ExternalId { get; set; } = default!;
    public string? ShortCode { get; set; }

    public string? Latitude { get; set; }
    public string? Longitude { get; set; }
    public List<ShiftFileDTO>? Photos { get; set; }

    public Dictionary<string, CustomField>? CustomFields { get; set; }

    [DepartmentHashIdConverter]
    public IEnumerable<ShiftEntitySelectDTO> Departments { get; set; } = new List<ShiftEntitySelectDTO>();

    [ServiceHashIdConverter]
    public IEnumerable<ShiftEntitySelectDTO> Services { get; set; } = new List<ShiftEntitySelectDTO>();

    [BrandHashIdConverter]
    public IEnumerable<ShiftEntitySelectDTO> Brands { get; set; } = new List<ShiftEntitySelectDTO>();

    public CompanyBranchDTO()
    {
        this.CustomFields = new();
    }
}


public class CompanyBranchValidator : AbstractValidator<CompanyBranchDTO>
{
    public CompanyBranchValidator()
    {
        RuleFor(x => x.Phone)
            .Custom((x, context) =>
            {
                if (x is not null && !ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(x))
                    context.AddFailure("Invalid Phone Number.");
            });
    }
}