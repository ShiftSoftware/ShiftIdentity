using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Entities;

[TemporalShiftEntity]
[Table("Companies", Schema = "ShiftIdentity")]
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

    public static implicit operator CompanyListDTO(Company entity)
    {
        if (entity == null)
            return default!;

        return new CompanyListDTO
        {
            ID = entity.ID.ToString(),
            Name = entity.Name,
            CompanyType = entity.CompanyType,
            ExternalId = entity.ExternalId,
            LegalName = entity.LegalName,
            ShortCode = entity.ShortCode,
        };
    }

    public static implicit operator CompanyDTO(Company entity)
    {
        if (entity == null)
            return default!;

        return new CompanyDTO
        {
            ID = entity.ID.ToString(),
            CreateDate = entity.CreateDate,
            CreatedByUserID = entity.CreatedByUserID.ToString(),
            IsDeleted = entity.IsDeleted,
            LastSaveDate = entity.LastSaveDate,
            LastSavedByUserID = entity.LastSavedByUserID.ToString(),

            Name = entity.Name,
            CompanyType = entity.CompanyType,
            ShortCode = entity.ShortCode,
            LegalName = entity.LegalName,
            ExternalId = entity.ExternalId,
            AlternativeExternalId = entity.AlternativeExternalId,
            HQAddress = entity.HQAddress,
            HQEmail = entity.HQEmail,
            HQPhone = entity.HQPhone,
            Logo = entity.Logo,
        };
    }
}
