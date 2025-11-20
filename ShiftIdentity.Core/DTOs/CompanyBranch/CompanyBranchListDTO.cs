using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;

[ShiftEntityKeyAndName(nameof(ID), nameof(Name))]
public class CompanyBranchListDTO : ShiftEntityListDTO
{
    [CompanyBranchHashIdConverter]
    public override string? ID { get; set; }

    [JsonConverter(typeof(LocalizedTextJsonConverter))]
    public string? Name { get; set; }
    public string? ShortCode { get; set; }
    public string? IntegrationId { get; set; }

    public DateTime? TerminationDate { get; set; }

    [CityHashIdConverter]
    public string? CityId { get; set; }

    [JsonConverter(typeof(LocalizedTextJsonConverter))]
    public string? City { get; set; }

    [RegionHashIdConverter]
    public string? RegionId { get; set; }

    [JsonConverter(typeof(LocalizedTextJsonConverter))]
    public string? Region { get; set; }

    [CompanyHashIdConverter]
    public string? CompanyId { get; set; }

    [JsonConverter(typeof(LocalizedTextJsonConverter))]
    public string? Company { get; set; }

    [DepartmentHashIdConverter]
    public IEnumerable<ShiftEntitySelectDTO> Departments { get; set; } = new List<ShiftEntitySelectDTO>();

    [ServiceHashIdConverter]
    public IEnumerable<ShiftEntitySelectDTO> Services { get; set; } = new List<ShiftEntitySelectDTO>();


    [BrandHashIdConverter]
    public List<ShiftEntitySelectDTO> Brands { get; set; } = new List<ShiftEntitySelectDTO>();
    public int? DisplayOrder { get; set; }
}
