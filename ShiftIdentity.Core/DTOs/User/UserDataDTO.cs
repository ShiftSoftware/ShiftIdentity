using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using FluentValidation;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftIdentity.Core.Localization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.User
{

    public class UserDataDTO : ShiftEntity.Model.Dtos.ShiftEntityViewAndUpsertDTO
    {
        [UserHashIdConverter]
        public override string? ID { get; set; }
        public string Username { get; set; } = default!;

        public string? Email { get; set; } = default!;

        
        public string? Phone { get; set; } = default!;

        public string FullName { get; set; } = default!;

        private DateTime? birthDate;
        public DateTime? BirthDate
        {
            get { return birthDate; }
            set { birthDate = value?.Date; }
        }

        public bool EmailVerified { get; set; }

        public bool PhoneVerified { get; set; }

        public List<ShiftFileDTO>? Signature { get; set; }
    }

    public class UserDataValidator: AbstractValidator<UserDataDTO>
    {
        public UserDataValidator(ShiftIdentityLocalizer localizer)
        {
            RuleFor(x=> x.Username)
                .NotEmpty().WithMessage(localizer["Please provide", localizer["Username"]])
                .MaximumLength(255).WithMessage(localizer["Your input cannot be more than 255 characters"]);

            RuleFor(x => x.Email)
                .EmailAddress().WithMessage(localizer["Invalid Email Address"])
                .MaximumLength(255).WithMessage(localizer["Your input cannot be more than 255 characters"]);

            RuleFor(x => x.FullName)
                .NotEmpty().WithMessage(localizer["Please provide", localizer["Full Name"]])
                .MaximumLength(255).WithMessage(localizer["Your input cannot be more than 255 characters"]);

            RuleFor(x => x.Phone)
                .Custom((x, context) =>
                {
                    if (x is not null && !ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(x))
                        context.AddFailure(localizer["Invalid Phone Number"]);
                });

            RuleFor(x => x.BirthDate)
                .Must(x=> x.HasValue ? x.Value.TimeOfDay == TimeSpan.Zero : true).WithMessage(localizer["Please provide a valid date"]);
        }
    }
}