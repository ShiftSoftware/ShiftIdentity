﻿
using ShiftSoftware.ShiftIdentity.Core.Enums;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data;

public class DBSeedOptions
{
    public string? RegionShortCode { get; set; }
    public string? RegionExternalId { get; set; }

    public string? CompanyShortCode { get; set; }
    public string? CompanyExternalId { get; set; }
    public string? CompanyAlternativeExternalId { get; set; }
    public CompanyTypes CompanyType { get; set; }

    public string? CompanyBranchShortCode { get; set; }
    public string? CompanyBranchExternalId { get; set; }
}