using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations.Schema;


namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("Departments", Schema = "ShiftIdentity")]
public class Department : ShiftEntity<Department>
{
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
}
