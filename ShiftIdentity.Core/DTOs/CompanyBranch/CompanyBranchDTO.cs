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

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
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


        RuleFor(x => x.Longitude)
            .NotNull()
            .When(x => x.PublishTargets!.Any())
            .WithMessage(localizer["Please provide", localizer["Longitude"]])
            .NotEmpty()
            .When(x => x.PublishTargets!.Any())
            .WithMessage(localizer["Please provide", localizer["Longitude"]])
            .ExclusiveBetween(-180, 180)
            .When(x => x.Longitude.HasValue || x.PublishTargets!.Any())
            .WithMessage(localizer["Longitude must be between -180 and 180"]);
            

        RuleFor(x => x.Latitude)
            .NotNull()
            .When(x => x.PublishTargets!.Any())
            .WithMessage(localizer["Please provide", localizer["Longitude"]])
            .NotEmpty()
            .When(x => x.PublishTargets!.Any())
            .WithMessage(localizer["Please provide", localizer["Latitude"]])
            .ExclusiveBetween(-90, 90)
            .When(x => x.Longitude.HasValue || x.PublishTargets!.Any())
            .WithMessage(localizer["Latitude must be between -90 and 90"]);;

        RuleFor(x=> x.DisplayName)
            .NotEmpty()
            .When(x=> x.PublishTargets!.Any())
            .WithMessage(localizer["Please provide", localizer["Display Name"]]);


    }
}