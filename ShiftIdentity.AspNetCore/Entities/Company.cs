﻿using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Entities;

[TemporalShiftEntity]
[Table("Companies", Schema = "ShiftIdentity")]
[DontSetCompanyInfoOnThisEntityWithAutoTrigger]
public class Company : ShiftEntity<Company>
{
    public string Name { get; set; } = default!;
    public string? LegalName { get; set; }
    public string? ExternalId { get; set; }
    public string? AlternativeExternalId { get; set; }
    public string? ShortCode { get; set; }
    public CompanyTypes CompanyType { get; set; }
    public string? Logo { get; set; }
    public string? HQPhone { get; set; }
    public string? HQEmail { get; set; }
    public string? HQAddress { get; set; }

    public virtual ICollection<CompanyBranch> CompanyBranches { get; set; }

    public Company()
    {
        this.CompanyBranches = new HashSet<CompanyBranch>();
    }

}
