﻿using FluentValidation;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

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

        var pattern = @"^-?\d+(\.\d+)?$";
        var regex = new Regex(pattern);

        RuleFor(x => x.Longitude)
            .Custom((x, context) =>
            {
                if (x is not null && !regex.IsMatch(x.ToString()))
                {
                    context.AddFailure(localizer["Longitude must be a decimal"]);
                    return;
                }
                 
                var longitude = float.Parse(x);
                if(x is not null && longitude > 180 || longitude < -180)
                    context.AddFailure(localizer["Longitude must be between -180 and 180"]);
            });

        RuleFor(x => x.Latitude)
            .Custom((x, context) =>
            {
                if (x is not null && !regex.IsMatch(x.ToString()))
                {
                    context.AddFailure(localizer["Latitude must be a decimal"]);
                    return;
                }

                var latitude = float.Parse(x);
                if (x is not null && latitude > 90 || latitude < -90)
                    context.AddFailure(localizer["Latitude must be between -90 and 90"]);
            });
            
    }
}