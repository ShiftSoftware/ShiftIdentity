using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Service;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

// Attribute-driven endpoint — see Brand for the full rationale. No controller, no repository class; feature
// locking is enforced centrally by FeatureLockSaveValidator. The route reproduces the old
// IdentityServiceController's "api/[controller]" output exactly.
[TemporalShiftEntity]
[Table("Services", Schema = "ShiftIdentity")]
[ShiftEntitySecureEndpoint<ServiceListDTO, ServiceDTO, ShiftIdentityActions>("api/IdentityService", nameof(ShiftIdentityActions.Services), UseGeneratedMapper = true)]
public class Service : ShiftEntity<Service>, IShiftEntityReplication
{
    /// <inheritdoc />
    public DateTimeOffset? LastReplicationDate { get; set; }

    /// <inheritdoc />
    public string? LastReplicationStamp { get; set; }

    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
}
