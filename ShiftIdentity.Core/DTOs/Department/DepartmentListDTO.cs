
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashId;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Department;

public class DepartmentListDTO : ShiftEntityListDTO
{
    [DepartmentHashIdConverter]
    public override string? ID { get; set; }
    public string? Name { get; set; }
}
