using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("Companies", Schema = "ShiftIdentity")]
[DontSetCompanyInfoOnThisEntityWithAutoTrigger]
public class Company : ShiftEntity<Company>
{
    public string Name { get; set; } = default!;
    public string? LegalName { get; set; }
    public string? IntegrationId { get; set; }
    public string? ShortCode { get; set; }
    public CompanyTypes CompanyType { get; set; }
    public string? Logo { get; set; }
    public string? HQPhone { get; set; }
    public string? HQEmail { get; set; }
    public string? HQAddress { get; set; }
    public string? Website { get; set; }
    public bool BuiltIn { get; set; }
    public DateTime? TerminationDate { get; set; }
    public Dictionary<string, CustomField>? CustomFields { get; set; }


    public long? ParentCompanyID { get; set; }
    public Company ParentCompany { get; set; }
    public virtual ICollection<Company> ChildCompanies { get; set; } = new HashSet<Company>();

    public virtual ICollection<CompanyBranch> CompanyBranches { get; set; }

    public Company()
    {
        CompanyBranches = new HashSet<CompanyBranch>();
        CustomFields = new();
    }

}
