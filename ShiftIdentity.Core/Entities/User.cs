﻿using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("Users", Schema = "ShiftIdentity")]
[DontSetCompanyInfoOnThisEntityWithAutoTrigger]
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

    public DateTimeOffset? LastSeen { get; set; }

    public new long RegionID { get; set; }
    public new long CompanyID { get; set; }
    public new long CompanyBranchID { get; set; }

    public virtual Region Region { get; set; }
    public virtual Company Company { get; set; }
    public virtual CompanyBranch CompanyBranch { get; set; }

    public virtual IEnumerable<UserAccessTree> AccessTrees { get; set; }
    public virtual ICollection<UserGroupUser> UserGroupUsers { get; set; } = new HashSet<UserGroupUser>();

    public User(long id) : base(id)
    {
        AccessTrees = new List<UserAccessTree>();
        UserGroupUsers = new HashSet<UserGroupUser>();
    }

    public User()
    {
        AccessTrees = new List<UserAccessTree>();
        UserGroupUsers = new HashSet<UserGroupUser>();
    }

}
