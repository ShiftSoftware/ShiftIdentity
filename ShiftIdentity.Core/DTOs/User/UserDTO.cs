using ShiftSoftware.ShiftEntity.Model.Dtos;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.User;

public class UserDTO : ShiftEntityDTO
{
    [_UserHashId]
    public override string? ID { get; set; }
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

    public IEnumerable<AccessTreeDTO> AccessTrees { get; set; }

    public UserDTO()
    {
        AccessTrees = new List<AccessTreeDTO>();
    }
}
