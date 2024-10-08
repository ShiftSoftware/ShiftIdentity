﻿using ShiftSoftware.ShiftEntity.Model.Dtos;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using FluentValidation;
using ShiftSoftware.ShiftIdentity.Core.Localization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.User;

public class UserDTO : ShiftEntityViewAndUpsertDTO
{
    [UserHashIdConverter]
    public override string? ID { get; set; }

    [Required]
    [CompanyBranchHashIdConverter]
    public ShiftEntitySelectDTO? CompanyBranchID { get; set; }

    #region Security
    [Required]
    [MaxLength(255)]
    public string Username { get; set; } = default!;

    public string? Password { get; set; } = default!;

    public bool IsActive { get; set; }

    public string? AccessTree { get; set; }

    #endregion

    #region Contacts
    [MaxLength(255)]
    [EmailAddress]
    public string? Email { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }
    #endregion

    #region Profile

    [Required]
    [MaxLength(255)]
    public string FullName { get; set; } = default!;

    private DateTime? birthDate;
    [DataType(DataType.Date)]
    public DateTime? BirthDate
    {
        get { return birthDate; }
        set { birthDate = value?.Date; }
    }

    #endregion

    [JsonHashIdConverter<AccessTreeDTO>(5)]
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
        RuleFor(x => x.Phone)
            .Custom((x, context) =>
            {
                if (x is not null && !ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(x))
                    context.AddFailure(localizer["InvalidPhoneNumber"]);
            });
    }
}