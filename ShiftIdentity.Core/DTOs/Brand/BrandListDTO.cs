using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using System.Text.Json.Serialization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Brand;

[ShiftEntityKeyAndName(nameof(ID), nameof(Name))]
public class BrandListDTO : ShiftEntityListDTO
{
    [BrandHashIdConverter]
    public override string? ID { get; set; }

    [JsonConverter(typeof(LocalizedTextJsonConverter))]
    public string Name { get; set; } = default!;
}
