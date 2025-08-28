using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using ShiftSoftware.ShiftEntity.Model.Flags;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("Users", Schema = "ShiftIdentity")]
public class User : ShiftEntity<User>, 
    IEntityHasCountry<User>, 
    IEntityHasRegion<User>,
    IEntityHasCompany<User>, 
    IEntityHasCompanyBranch<User>
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

    public string? VerificationSASToken { get; set; }

    #endregion

    #region Contacts
    [MaxLength(255)]
    public string? Email { get; set; }
    public bool EmailVerified { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }
    public bool PhoneVerified { get; set; }
    #endregion

    #region Profile

    [Required]
    [MaxLength(255)]
    public string FullName { get; set; } = default!;

    public DateTime? BirthDate { get; set; }
    #endregion

    public DateTimeOffset? LastSeen { get; set; }

    public long? CountryID { get; set; }
    public long? RegionID { get; set; }
    public long? CompanyID { get; set; }
    public long? CompanyBranchID { get; set; }
    public string? Signature { get; set; }

    public virtual Country? Country { get; set; }
    public virtual Region? Region { get; set; }
    public virtual Company? Company { get; set; }
    public virtual CompanyBranch? CompanyBranch { get; set; }
    public virtual UserLog UserLog { get; set; }

    public virtual IEnumerable<UserAccessTree> AccessTrees { get; set; }
    public virtual ICollection<TeamUser> TeamUsers { get; set; } = new HashSet<TeamUser>();

    public User(long id) : base(id)
    {
        AccessTrees = new List<UserAccessTree>();
        TeamUsers = new HashSet<TeamUser>();
    }

    public User()
    {
        AccessTrees = new List<UserAccessTree>();
        TeamUsers = new HashSet<TeamUser>();
    }

}
