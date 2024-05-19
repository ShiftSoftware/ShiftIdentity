using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Core.Flags;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("Teams", Schema = "ShiftIdentity")]
public class Team : ShiftEntity<Team>, IEntityHasCompany<Team>
{
    [Required]
    public string Name { get; set; } = default!;

    public string? IntegrationId { get; set; }

    public virtual ICollection<TeamUser> TeamUsers { get; set; } = new HashSet<TeamUser>();
    public long? CompanyID { get; set; }

    public Team()
    {
        TeamUsers = new HashSet<TeamUser>();
    }
}
