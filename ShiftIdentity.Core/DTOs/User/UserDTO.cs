using FluentValidation;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.User;

public class UserDTO : ShiftEntityViewAndUpsertDTO
{
    [UserHashIdConverter]
    public override string? ID { get; set; }

    [CompanyBranchHashIdConverter]
    public ShiftEntitySelectDTO? CompanyBranchID { get; set; }

    #region Security
    public string Username { get; set; } = default!;

    public string? Password { get; set; } = default!;

    public bool IsActive { get; set; }

    public string? AccessTree { get; set; }

    #endregion

    #region Contacts
    public string? Email { get; set; }

    public string? Phone { get; set; }
    #endregion

    #region Profile

    public string FullName { get; set; } = default!;

    private DateTime? birthDate;
    public DateTime? BirthDate
    {
        get { return birthDate; }
        set { birthDate = value?.Date; }
    }

    #endregion

    [AccessTreeHashIdConverter]
    public IEnumerable<ShiftEntitySelectDTO> AccessTrees { get; set; }

    public UserDTO()
    {
        AccessTrees = new List<ShiftEntitySelectDTO>();
    }
}

public class UserValidator : AbstractValidator<UserDTO>
{
    public UserValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.Password)
            .MinimumLength(6).WithMessage(localizer["The password must be at least n characters long", 6])
            .MaximumLength(255).WithMessage(localizer["Your input cannot be more than 255 characters"])
            .Matches(@"^(?=.*[0-9]).+$").WithMessage(localizer["The password must contain at least one digit"])
            .Must(HaveRequiredUniqueChars).WithMessage(localizer["The password must contain at least 3 unique characters"])
            .When(x => !string.IsNullOrWhiteSpace(x.Password));

        RuleFor(x => x.Username)
                .NotEmpty().WithMessage(localizer["Please provide", localizer["Username"]])
                .MaximumLength(255).WithMessage(localizer["Your input cannot be more than 255 characters"]);

        RuleFor(x => x.Email)
            .EmailAddress()
            .WithMessage(localizer["Invalid Email Address"])
            .MaximumLength(255).WithMessage(localizer["Your input cannot be more than 255 characters"])
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage(localizer["Please provide", localizer["Full Name"]])
            .MaximumLength(255).WithMessage(localizer["Your input cannot be more than 255 characters"]);

        RuleFor(x => x.Phone)
            .Custom((x, context) =>
            {
                if (x is not null && !ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(x))
                    context.AddFailure(localizer["Invalid Phone Number"]);
            })
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));

        RuleFor(x => x.BirthDate)
            .Must(x => x.HasValue ? x.Value.TimeOfDay == TimeSpan.Zero : true)
            .WithMessage(localizer["Please provide a valid date"])
            .When(x => x.BirthDate != null);

        RuleFor(x => x.CompanyBranchID)
            .NotNull().WithMessage(localizer["Please select", localizer["Company Branch"]]);
    }

    private bool HaveRequiredUniqueChars(string password)
    {
        return password.Distinct().Count() >= 3;
    }
}