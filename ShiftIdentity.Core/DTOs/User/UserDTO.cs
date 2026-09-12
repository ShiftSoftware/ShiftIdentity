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

    /// <summary>
    /// Applies only when <see cref="Password"/> is supplied: ask the user to change that password at their next
    /// sign-in. Checked by default, which keeps the previous forced-change behaviour; an administrator can turn it
    /// off per save. Named after the staged v2 <c>AdminSetPasswordRequest</c> flag so the later adapter maps 1:1.
    /// </summary>
    public bool RequireChangeAtNextLogin { get; set; } = true;

    public bool IsActive { get; set; }

    public string? AccessTree { get; set; }

    public bool TotpEnabled { get; set; }

    #endregion

    #region Contacts
    public string? Email { get; set; }

    /// <summary>
    /// Send a verification link to a NEW address: the address of a created user, or a changed address on an
    /// update. An unchanged address never triggers a send. Checked by default. Named after the staged v2
    /// <c>AdminEmailChangeRequest</c> flag so the later adapter maps 1:1.
    /// </summary>
    public bool SendVerification { get; set; } = true;

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

    public string? IntegrationId { get; set; }

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
        // The same policy the staged authority applies when it stores the password: 15–128 code points, no control
        // characters, not a common password and not based on the username. There are no composition rules.
        RuleFor(x => x.Password)
            .Custom((password, context) =>
            {
                if (new Authentication.NewPasswordPolicy().Validate(password, context.InstanceToValidate.Username ?? "") is { } failure)
                    context.AddFailure(failure == Authentication.PasswordPolicyFailure.TooShort
                        ? localizer["The password must be at least n characters long", Authentication.NewPasswordPolicy.MinimumLength]
                        : localizer[Authentication.NewPasswordPolicy.Describe(failure)]);
            })
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

}