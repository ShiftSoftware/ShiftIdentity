using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using System.ComponentModel.DataAnnotations.Schema;
using ShiftSoftware.ShiftEntity.Model.Dtos;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Entities;

[TemporalShiftEntity]
[Table("Users", Schema = "ShiftIdentity")]
public class User : ShiftEntity<User>
{
    #region Security

    [Required]
    [MaxLength(255)]
    public string Username { get; set; } = default!;

    public byte[] PasswordHash { get; set; } = default!;

    public byte[] Salt { get; set; } = default!;

    public int LoginAttempts { get; set; }

    public DateTime? LockDownUntil { get; set; }

    public bool IsSuperAdmin { get; set; }

    public bool IsActive { get; set; }

    public bool BuiltIn { get; set; }

    public string? AccessTree { get; set; }

    public bool RequireChangePassword { get; set; }

    #endregion

    #region Contacts
    [MaxLength(255)]
    public string? Email { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }
    #endregion

    #region Profile

    [Required]
    [MaxLength(255)]
    public string FullName { get; set; } = default!;

    public DateTime? BirthDate { get; set; }
    #endregion

    public IEnumerable<UserAccessTree> AccessTrees { get; set; }

    public User(long id) : base(id)
    {
        AccessTrees = new List<UserAccessTree>();
    }

    public User()
    {
        AccessTrees = new List<UserAccessTree>();
    }

}
