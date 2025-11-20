using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using System.Text.Json.Serialization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.City;

[ShiftEntityKeyAndName(nameof(ID), nameof(Name))]
public class CityListDTO : ShiftEntityListDTO
{
    [CityHashIdConverter]
    public override string? ID { get; set; }

    [JsonConverter(typeof(LocalizedTextJsonConverter))]
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }

    [JsonConverter(typeof(LocalizedTextJsonConverter))]
    public string Region { get; set; } = default!;


    [JsonConverter(typeof(LocalizedTextJsonConverter))]
    public string Country { get; set; } = default!;


    [CountryHashIdConverter]
    public string? CountryID { get; set; }

    [RegionHashIdConverter]
    public string? RegionID { get; set; }
    public int? DisplayOrder { get; set; }
}
