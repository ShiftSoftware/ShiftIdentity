﻿using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using System.Collections.Generic;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;

public class CompanyBranchListDTO : ShiftEntityListDTO
{
    [CompanyBranchHashIdConverter]
    public override string? ID { get; set; }

    public string? Name { get; set; }
    public string? ShortCode { get; set; }
    public string? ExternalId { get; set; } = default!;
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? Company { get; set; }

    [DepartmentHashIdConverter]
    public IEnumerable<ShiftEntitySelectDTO> Departments { get; set; } = new List<ShiftEntitySelectDTO>();
}
