using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Flags;
using ShiftSoftware.ShiftEntity.Model.Replication;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

[TemporalShiftEntity]
[Table("Teams", Schema = "ShiftIdentity")]
public class Team : ShiftEntity<Team>, IEntityHasCompany<Team>, IEntityHasTeam<Team>, IShiftEntityReplication
{
    /// <inheritdoc />
    public DateTimeOffset? LastReplicationDate { get; set; }

    /// <inheritdoc />
    public string? LastReplicationStamp { get; set; }

    [Required]
    public string Name { get; set; } = default!;

    public string? IntegrationId { get; set; }

    public virtual ICollection<TeamUser> TeamUsers { get; set; } = new HashSet<TeamUser>();
    public long? CompanyID { get; set; }

    public virtual Company? Company { get; set; } = default!;
    public virtual ICollection<TeamCompanyBranch> TeamCompanyBranches { get; set; } = new HashSet<TeamCompanyBranch>();
    public List<string> Tags { get; set; } = new();

    public long? TeamID { get; set; }

    public Team()
    {
        TeamUsers = new HashSet<TeamUser>();
        TeamCompanyBranches = new HashSet<TeamCompanyBranch>();
    }
}
