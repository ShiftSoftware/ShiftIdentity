﻿using ShiftSoftware.ShiftEntity.Core;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("CompanyBranches", Schema = "ShiftIdentity")]
[DontSetCompanyInfoOnThisEntityWithAutoTrigger]
public class CompanyBranch : ShiftEntity<CompanyBranch>
{
    public string Name { get; set; } = default!;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? ExternalId { get; set; } = default!;
    public string? ShortCode { get; set; }
    public virtual Company Company { get; set; } = default!;
    public virtual Region Region { get; set; } = default!;
    public virtual ICollection<CompanyBranchDepartment> CompanyBranchDepartments { get; set; }
    public virtual ICollection<CompanyBranchService> CompanyBranchServices { get; set; }

    public CompanyBranch()
    {
        CompanyBranchDepartments = new HashSet<CompanyBranchDepartment>();
        CompanyBranchServices = new HashSet<CompanyBranchService>();
    }

}