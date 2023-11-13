
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.City;

public class CityListDTO : ShiftEntityListDTO
{
    [CityHashIdConverter]
    public override string? ID { get; set; }
    public string Name { get; set; } = default!;
    public string Region { get; set; } = default!;
}
