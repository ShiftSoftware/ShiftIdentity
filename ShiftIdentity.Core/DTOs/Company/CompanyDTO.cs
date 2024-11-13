using FluentValidation;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.Enums;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Company;

public class CompanyDTO : ShiftEntityViewAndUpsertDTO
{
    [CompanyHashIdConverter]
    public override string? ID { get; set; }
    public string Name { get; set; } = default!;
    public string? ShortCode { get; set; }
    public string? LegalName { get; set; }
    public string? IntegrationId { get; set; }
    public string? AlternativeExternalId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CompanyTypes CompanyType { get; set; }
    public string? Logo { get; set; }
    public string? HQPhone { get; set; }
    public string? HQEmail { get; set; }
    public string? HQAddress { get; set; }
    public Dictionary<string, CustomFieldDTO>? CustomFields { get; set; }

    public CompanyDTO()
    {
        this.CustomFields = new();
    }
}

public class CompanyValidator : AbstractValidator<CompanyDTO>
{
    public CompanyValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.HQPhone)
            .Custom((x, context) =>
            {
                if (x is not null && !ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(x))
                    context.AddFailure(localizer["Invalid Phone Number"]);
            });

        RuleFor(x=> x.HQEmail)
            .EmailAddress().WithMessage(localizer["Invalid Email Address"]);

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(localizer["Please enter a name"]);

        RuleFor(x => x.CompanyType)
            .IsInEnum().WithMessage(localizer["Please select", localizer["Company Type"]]);
    }
}