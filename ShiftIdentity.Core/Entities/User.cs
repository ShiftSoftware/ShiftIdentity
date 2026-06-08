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

    public string? IntegrationId { get; set; }

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

    /// <summary>
    /// Versions the user's refresh tokens: each embeds the stamp current at mint time and
    /// <c>AuthService.RefreshAsync</c> rejects any whose stamp no longer matches. So
    /// <see cref="RegenerateSecurityStamp"/> invalidates every outstanding session — what lets a
    /// password change / "log out everywhere" kill otherwise non-revocable refresh tokens.
    /// <para>
    /// Defaults to <see cref="Guid.Empty"/> (not randomized in the ctor): a fresh stamp per
    /// <c>new User()</c> would break stateless repositories that rebuild the same user each call
    /// (e.g. <c>FakeUserRepository</c>). Empty is a stable baseline — the check is per-user.
    /// </para>
    /// </summary>
    public Guid SecurityStamp { get; set; }

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

    /// <summary>
    /// Rolls the <see cref="SecurityStamp"/>, invalidating every refresh token previously issued
    /// to this user. Call this on any credential change (password change/reset) or whenever all
    /// of the user's sessions must be terminated.
    /// </summary>
    public void RegenerateSecurityStamp() => SecurityStamp = Guid.NewGuid();
}
