using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Brand;

[ShiftEntityKeyAndName(nameof(ID), nameof(Name))]
public class BrandListDTO : ShiftEntityListDTO
{
    [ServiceHashIdConverter]
    public override string? ID { get; set; }
    public string Name { get; set; } = default!;
}
