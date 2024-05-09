using ShiftSoftware.ShiftEntity.Core;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("UserGroups", Schema = "ShiftIdentity")]
public class UserGroup : ShiftEntity<UserGroup>
{
    [Required]
    public string Name { get; set; }

    public string? IntegrationId { get; set; }

    public virtual ICollection<UserGroupUser> UserGroupUsers { get; set; } = new HashSet<UserGroupUser>();

    public UserGroup()
    {
        UserGroupUsers = new HashSet<UserGroupUser>();
    }
}
