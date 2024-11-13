using FluentValidation;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.User;

public class UserInfoDTO : UserListDTO
{
    public DateTime? BirthDate { get; set; }

    public string? PlainTextPassword { get; set; }
}

public class UserInfoValidator : AbstractValidator<UserInfoDTO>
{
    public UserInfoValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.BirthDate)
            .Must(x => x.HasValue ? x.Value.TimeOfDay == TimeSpan.Zero : true).WithMessage(localizer["Please provide a valid date"]);

    }
}