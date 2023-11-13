
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.City;

[ShiftEntityKeyAndName(nameof(ID), nameof(Name))]
public class CityListDTO : ShiftEntityListDTO
{
    [CityHashIdConverter]
    public override string? ID { get; set; }
    public string Name { get; set; } = default!;
    public string Region { get; set; } = default!;
}
