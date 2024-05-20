using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using System.Text.Json.Serialization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Team;

[ShiftEntityKeyAndName(nameof(ID), nameof(Name))]
public class TeamListDTO : ShiftEntityListDTO
{
    [TeamHashIdConverter]
    public override string? ID { get; set; }
    public string Name { get; set; } = default!;

    public string? IntegrationId { get; set; }

    [JsonConverter(typeof(LocalizedTextJsonConverter))]
    public string? Company { get; set; }
}
