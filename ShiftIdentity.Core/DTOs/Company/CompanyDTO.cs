using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashId;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Company;

public class CompanyDTO : ShiftEntityDTO
{
    [CompanyHashIdConverter]
    public override string? ID { get; set; }


    [Required]
    public string Name { get; set; } = default!;
    public string? ShortCode { get; set; }
    public string? LegalName { get; set; }
    public string? ExternalId { get; set; }
    public string? AlternativeExternalId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    [Range(1, int.MaxValue, ErrorMessage = "Required")]
    public CompanyTypes CompanyType { get; set; }
    public string? Logo { get; set; }
    public string? HQPhone { get; set; }
    public string? HQEmail { get; set; }
    public string? HQAddress { get; set; }
}