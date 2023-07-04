using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;
using System.ComponentModel.DataAnnotations.Schema;


namespace ShiftSoftware.ShiftIdentity.AspNetCore.Entities;

[TemporalShiftEntity]
[Table("Departments", Schema = "ShiftIdentity")]
public class Department : ShiftEntity<Department>
{
    public string Name { get; set; } = default!;

    public static implicit operator DepartmentListDTO(Department entity)
    {
        if (entity == null)
            return default!;

        return new DepartmentListDTO
        {
            ID = entity.ID.ToString(),
            Name = entity.Name,
        };
    }

    public static implicit operator DepartmentDTO(Department entity)
    {
        if (entity == null)
            return default!;

        return new DepartmentDTO
        {
            ID = entity.ID.ToString(),
            CreateDate = entity.CreateDate,
            CreatedByUserID = entity.CreatedByUserID.ToString(),
            IsDeleted = entity.IsDeleted,
            LastSaveDate = entity.LastSaveDate,
            LastSavedByUserID = entity.LastSavedByUserID.ToString(),
            
            Name = entity.Name
        };
    }
}
