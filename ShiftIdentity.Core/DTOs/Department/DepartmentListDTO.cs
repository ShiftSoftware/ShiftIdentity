
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using System.Text.Json.Serialization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Department;

[ShiftEntityKeyAndName(nameof(ID), nameof(Name))]
public class DepartmentListDTO : ShiftEntityListDTO
{
    [DepartmentHashIdConverter]
    public override string? ID { get; set; }

    [JsonConverter(typeof(LocalizedTextJsonConverter))]
    public string? Name { get; set; }

    public string? IntegrationId { get; set; }
}
