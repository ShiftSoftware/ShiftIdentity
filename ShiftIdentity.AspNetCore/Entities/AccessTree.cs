using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Entities;

[TemporalShiftEntity]
public class AccessTree : ShiftEntity<AccessTree>
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = default!;

    [Required]
    public string Tree { get; set; } = default!;

    public bool BuiltIn { get; set; }

    //public IEnumerable<UserAccessTree> Users { get; set; }

    public AccessTree()
    {
        //Users = new List<UserAccessTree>();
    }

    public static implicit operator AccessTreeDTO(AccessTree entity)
    {
        if (entity == null)
            return default!;

        return new AccessTreeDTO
        {
            CreateDate = entity.CreateDate,
            CreatedByUserID = entity.CreatedByUserID.ToString(),
            ID = entity.ID.ToString(),
            IsDeleted = entity.IsDeleted,
            LastSaveDate = entity.LastSaveDate,
            LastSavedByUserID = entity.LastSavedByUserID.ToString(),

            Name = entity.Name,
            Tree = entity.Tree
        };
    }
}
