using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Core.Flags;
using ShiftSoftware.ShiftEntity.Model;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("CompanyBranches", Schema = "ShiftIdentity")]
[DontSetCompanyInfoOnThisEntityWithAutoTrigger]
public class CompanyBranch : 
    ShiftEntity<CompanyBranch>, IEntityHasRegion<CompanyBranch>, IEntityHasCompany<CompanyBranch>, IEntityHasCountry<CompanyBranch>
{
    public string Name { get; set; } = default!;
    public string? Phone { get; set; }
    public string? ShortPhone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Latitude { get; set; }
    public string? Longitude { get; set; }
    public string? Photos { get; set; }
    public string? WorkingHours { get; set; }
    public string? WorkingDays { get; set; }
    public string? IntegrationId { get; set; } = default!;
    public string? ShortCode { get; set; }
    public bool BuiltIn { get; set; }

    public Dictionary<string, CustomField>? CustomFields { get; set; }

    public virtual Company? Company { get; set; } = default!;
    public virtual Region? Region { get; set; } = default!;
    public virtual City City { get; set; } = default!;
    public virtual ICollection<CompanyBranchDepartment>? CompanyBranchDepartments { get; set; }
    public virtual ICollection<CompanyBranchService>? CompanyBranchServices { get; set; }
    public virtual ICollection<CompanyBranchBrand>? CompanyBranchBrands { get; set; }
    public virtual ICollection<User> Users { get; set; }

    public long? RegionID { get; set; }
    public long? CityID { get; set; }
    public long? CompanyID { get; set; }
    public long? CountryID { get; set; }

    public CompanyBranch()
    {
        CompanyBranchDepartments = new HashSet<CompanyBranchDepartment>();
        CompanyBranchServices = new HashSet<CompanyBranchService>();
        CompanyBranchBrands = new HashSet<CompanyBranchBrand>();
        Users = new HashSet<User>();
        CustomFields = new();
    }

}
