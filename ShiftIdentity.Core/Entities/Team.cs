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

    public virtual Company? Company { get; set; } = default!;
    public virtual ICollection<TeamCompanyBranch> TeamCompanyBranches { get; set; } = new HashSet<TeamCompanyBranch>();
    public List<string> Tags { get; set; } = new();

    public Team()
    {
        TeamUsers = new HashSet<TeamUser>();
        TeamCompanyBranches = new HashSet<TeamCompanyBranch>();
    }
}
