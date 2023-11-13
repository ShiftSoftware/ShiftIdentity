using ShiftSoftware.ShiftEntity.Model.Dtos;
using System.ComponentModel.DataAnnotations;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftEntity.Core;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;

[ShiftEntityKeyAndName(nameof(ID), nameof(Name))]
public class AccessTreeDTO : ShiftEntityMixedDTO
{
    [JsonHashIdConverter<AccessTreeDTO>(5)]
    public override string? ID { get; set; }
    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = default!;

    [Required]
    public string Tree { get; set; } = default!;
}
