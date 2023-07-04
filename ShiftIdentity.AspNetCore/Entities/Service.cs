
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Service;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Entities;

[TemporalShiftEntity]
[Table("Services", Schema = "ShiftIdentity")]
public class Service : ShiftEntity<Service>
{
    public string Name { get; set; } = default!;

    public static implicit operator ServiceListDTO(Service entity)
    {
        if (entity == null)
            return default!;

        return new ServiceListDTO
        {
            ID = entity.ID.ToString(),
            Name = entity.Name
        };
    }

    public static implicit operator ServiceDTO(Service entity)
    {
        if (entity == null)
            return default!;

        return new ServiceDTO
        {
            ID = entity.ID.ToString(),
            CreateDate = entity.CreateDate,
            CreatedByUserID = entity.CreatedByUserID.ToString(),
            IsDeleted = entity.IsDeleted,
            LastSaveDate = entity.LastSaveDate,
            LastSavedByUserID = entity.LastSavedByUserID.ToString(),

            Name = entity.Name,
        };
    }
}
