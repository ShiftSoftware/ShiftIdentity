using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashId;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using System.Text.Json.Serialization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Company;

public class CompanyListDTO : ShiftEntityListDTO
{
    [CompanyHashIdConverter]
    public override string? ID { get; set; }

    public string? Name { get; set; }
    public string? LegalName { get; set; }
    public string? ShortCode { get; set; }
    public string? ExternalId { get; set; } = default!;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CompanyTypes? CompanyType { get; set; }
}
