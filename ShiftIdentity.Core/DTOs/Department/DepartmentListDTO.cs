
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Department;

[ShiftEntityKeyAndName(nameof(ID), nameof(Name))]
public class DepartmentListDTO : ShiftEntityListDTO
{
    [DepartmentHashIdConverter]
    public override string? ID { get; set; }
    public string? Name { get; set; }
}
