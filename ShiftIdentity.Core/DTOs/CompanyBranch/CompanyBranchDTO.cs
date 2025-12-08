using FluentValidation;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.Enums;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

    public List<TaggedTextDTO> Phones { get; set; } = new();

    public string? ShortPhone { get; set; }

    public DateTime? TerminationDate { get; set; }

    public string? Email { get; set; }
    public List<TaggedTextDTO> Emails { get; set; } = new();
    public string? Address { get; set; }
    public string? IntegrationId { get; set; } = default!;
    public string? ShortCode { get; set; }

    public string? Latitude { get; set; }
    public string? Longitude { get; set; }
    public List<ShiftFileDTO>? Photos { get; set; }
    public List<ShiftFileDTO>? MobilePhotos { get; set; }
    public string? WorkingHours { get; set; }
    public string? WorkingDays { get; set; }
    public Dictionary<string, CustomFieldDTO>? CustomFields { get; set; }

    [DepartmentHashIdConverter]
    public List<ShiftEntitySelectDTO> Departments { get; set; } = new List<ShiftEntitySelectDTO>();

    [ServiceHashIdConverter]
    public List<ShiftEntitySelectDTO> Services { get; set; } = new List<ShiftEntitySelectDTO>();

    [BrandHashIdConverter]
    public List<ShiftEntitySelectDTO> Brands { get; set; } = new List<ShiftEntitySelectDTO>();
    public int? DisplayOrder { get; set; }
    public string? DisplayName { get; set; } = default!;
    public IEnumerable<PublishTarget>? PublishTargets { get; set; } = new HashSet<PublishTarget>();

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

        //RuleFor(x => x.Phone)
        //    .Custom((x, context) =>
        //    {
        //        if (x is not null && !string.IsNullOrWhiteSpace(x) && !ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(x))
        //            context.AddFailure(localizer["Invalid Phone Number"]);
        //    });

        var pattern = @"^-?\d+(\.\d+)?$";
        var regex = new Regex(pattern);

        RuleFor(x => x.Longitude)
            .Custom((x, context) =>
            {
                if(string.IsNullOrWhiteSpace(x) && context.InstanceToValidate.PublishTargets is not null && context.InstanceToValidate.PublishTargets.Any())
                {
                    context.AddFailure(localizer["Please provide", localizer["Longitude"]]);
                    return;
                }


                if (!string.IsNullOrWhiteSpace(x))
                {
                    if (!float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) || !regex.IsMatch(x))
                    {
                        context.AddFailure(localizer["Longitude must be a decimal"]);
                        return;
                    }

                    if (longitude > 180 || longitude < -180)
                    {
                        context.AddFailure(localizer["Longitude must be between -180 and 180"]);
                        return;
                    }
                }
            })
            .When(x => x.Longitude is not null || (x.PublishTargets is not null && x.PublishTargets.Any()));

        RuleFor(x => x.Latitude)
            .Custom((x, context) =>
            {
               if (string.IsNullOrWhiteSpace(x) && context.InstanceToValidate.PublishTargets is not null && context.InstanceToValidate.PublishTargets.Any())
                {
                    context.AddFailure(localizer["Please provide", localizer["Latitude"]]);
                    return;
                }


                if (!string.IsNullOrWhiteSpace(x))
                {
                    if (!float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) || !regex.IsMatch(x))
                    {
                        context.AddFailure(localizer["Longitude must be a decimal"]);
                        return;
                    }

                    if (latitude > 90 || latitude < -90)
                    {
                        context.AddFailure(localizer["Latitude must be between -90 and 90"]);
                        return;
                    }
                }
            })
            .When(x => x.Latitude is not null || (x.PublishTargets is not null && x.PublishTargets.Any()));
           
    }
}