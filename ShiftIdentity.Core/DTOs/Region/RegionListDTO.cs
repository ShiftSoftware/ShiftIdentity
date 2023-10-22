
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Region;

public class RegionListDTO : ShiftEntityListDTO
{
    [RegionHashIdConverter]
    public override string? ID { get; set; }

    public string Name { get; set; } = default!;
    public string? ExternalId { get; set; } = default!;
    public string? ShortCode { get; set; }
}
