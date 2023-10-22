using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Service;

public class ServiceListDTO : ShiftEntityListDTO
{
    [ServiceHashIdConverter]
    public override string? ID { get; set; }
    public string Name { get; set; } = default!;
}
