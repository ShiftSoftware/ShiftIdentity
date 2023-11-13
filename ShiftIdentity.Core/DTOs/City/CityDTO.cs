
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.City;

public class CityDTO : ShiftEntityViewAndUpsertDTO
{
    [CityHashIdConverter]
    public override string? ID { get; set; }

    [Required]
    public string Name { get; set; } = default!;

    [Required]
    [RegionHashIdConverter]
    public ShiftEntitySelectDTO Region { get; set; } = default!;
}
