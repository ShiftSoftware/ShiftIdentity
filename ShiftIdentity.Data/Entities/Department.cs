using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;
using System;
using System.ComponentModel.DataAnnotations.Schema;


using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

// Attribute-driven endpoint — see Brand for the full rationale. No controller, no repository class; feature
// locking is enforced centrally by FeatureLockSaveValidator. The route reproduces the old
// IdentityDepartmentController's "api/[controller]" output exactly.
[TemporalShiftEntity]
[Table("Departments", Schema = "ShiftIdentity")]
[ShiftEntitySecureEndpoint<DepartmentListDTO, DepartmentDTO, ShiftIdentityActions>("api/IdentityDepartment", nameof(ShiftIdentityActions.Departments), UseGeneratedMapper = true)]
public class Department : ShiftEntity<Department>, IShiftEntityReplication
{
    /// <inheritdoc />
    public DateTimeOffset? LastReplicationDate { get; set; }

    /// <inheritdoc />
    public string? LastReplicationStamp { get; set; }

    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
}
