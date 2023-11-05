
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Department;

public class DepartmentDTO : ShiftEntityViewAndUpsertDTO
{
    [DepartmentHashIdConverter]
    public override string? ID { get; set; }

    [Required]
    public string Name { get; set; } = default!;
}
