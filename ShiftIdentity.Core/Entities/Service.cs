using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Replication;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("Services", Schema = "ShiftIdentity")]
public class Service : ShiftEntity<Service>, IShiftEntityReplication
{
    /// <inheritdoc />
    public DateTimeOffset? LastReplicationDate { get; set; }

    /// <inheritdoc />
    public string? LastReplicationStamp { get; set; }

    [Column(TypeName = "json")]
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
}
