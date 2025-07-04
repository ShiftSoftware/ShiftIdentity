﻿
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using System.Text.Json.Serialization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Region;

[ShiftEntityKeyAndName(nameof(ID), nameof(Name))]
public class RegionListDTO : ShiftEntityListDTO
{
    [RegionHashIdConverter]
    public override string? ID { get; set; }

    [JsonConverter(typeof(LocalizedTextJsonConverter))]
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
    public string? ShortCode { get; set; }

    [JsonConverter(typeof(LocalizedTextJsonConverter))]
    public string? Country { get; set; } = default!;


    [CountryHashIdConverter]
    public string? CountryID { get; set; }
}