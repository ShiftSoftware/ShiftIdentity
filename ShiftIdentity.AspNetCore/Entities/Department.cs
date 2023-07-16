using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;
using System.ComponentModel.DataAnnotations.Schema;


namespace ShiftSoftware.ShiftIdentity.AspNetCore.Entities;

[TemporalShiftEntity]
[Table("Departments", Schema = "ShiftIdentity")]
public class Department : ShiftEntity<Department>
{
    public string Name { get; set; } = default!;

}
