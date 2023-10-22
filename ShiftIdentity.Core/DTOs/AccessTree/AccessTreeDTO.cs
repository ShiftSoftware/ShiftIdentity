using ShiftSoftware.ShiftEntity.Model.Dtos;
using System.ComponentModel.DataAnnotations;
using ShiftSoftware.ShiftEntity.Model.HashIds;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;

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
