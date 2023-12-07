using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.Enums;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using System.Text.Json.Serialization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Company;

[ShiftEntityKeyAndName(nameof(ID), nameof(Name))]
public class CompanyListDTO : ShiftEntityListDTO
{
    [CompanyHashIdConverter]
    public override string? ID { get; set; }

    [JsonConverter(typeof(LocalizedTextJsonConverter))]
    public string? Name { get; set; }

    [JsonConverter(typeof(LocalizedTextJsonConverter))]
    public string? LegalName { get; set; }
    public string? ShortCode { get; set; }
    public string? ExternalId { get; set; } = default!;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CompanyTypes? CompanyType { get; set; }
}
