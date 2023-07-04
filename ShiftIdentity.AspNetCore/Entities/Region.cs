
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Entities;

[TemporalShiftEntity]
[Table("Regions", Schema = "ShiftIdentity")]
public class Region : ShiftEntity<Region>
{
    public string Name { get; set; } = default!;
    public string? ExternalId { get; set; } = default!;
    public string? ShortCode { get; set; }

    public virtual ICollection<CompanyBranch> CompanyBranches { get; set; }

    public Region()
    {
        this.CompanyBranches = new HashSet<CompanyBranch>();
    }

    public static implicit operator RegionListDTO(Region entity)
    {
        if (entity == null)
            return default!;

        return new RegionListDTO
        {
            ID = entity.ID.ToString(),
            Name = entity.Name,
            ShortCode = entity.ShortCode,
            ExternalId = entity.ExternalId
        };
    }

    public static implicit operator RegionDTO(Region entity)
    {
        if (entity == null)
            return default!;

        return new RegionDTO
        {
            ID = entity.ID.ToString(),
            CreateDate = entity.CreateDate,
            CreatedByUserID = entity.CreatedByUserID.ToString(),
            IsDeleted = entity.IsDeleted,
            LastSaveDate = entity.LastSaveDate,
            LastSavedByUserID = entity.LastSavedByUserID.ToString(),

            Name = entity.Name,
            ShortCode = entity.ShortCode,
            ExternalId = entity.ExternalId,
        };
    }
}
