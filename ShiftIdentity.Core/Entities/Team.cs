using ShiftSoftware.ShiftEntity.Core;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("Teams", Schema = "ShiftIdentity")]
public class Team : ShiftEntity<Team>
{
    [Required]
    public string Name { get; set; }

    public string? IntegrationId { get; set; }

    public virtual ICollection<TeamUser> TeamUsers { get; set; } = new HashSet<TeamUser>();

    public Team()
    {
        TeamUsers = new HashSet<TeamUser>();
    }
}
