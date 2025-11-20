using FluentValidation;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Country;

public class CountryDTO : ShiftEntityViewAndUpsertDTO
{
    [CountryHashIdConverter]
    public override string? ID { get; set; }

    [Required]
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
    public string? ShortCode { get; set; }

    [Required]
    public string CallingCode { get; set; } = default!;

    public List<ShiftFileDTO>? Flag { get; set; }
    public int? DisplayOrder { get; set; }
}

public class CountryValidator : AbstractValidator<CountryDTO> {

    public CountryValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x=> x.Name)
            .NotEmpty().WithMessage(localizer["Please enter a name"]);

        RuleFor(x=> x.CallingCode)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["Calling Code"]]);
    }
}