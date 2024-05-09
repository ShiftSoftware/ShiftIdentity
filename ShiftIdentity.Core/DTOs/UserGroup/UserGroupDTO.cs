using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.UserGroup;

public class UserGroupDTO : ShiftEntityViewAndUpsertDTO
{
    [UserGroupHashIdConverter]
    public override string? ID { get; set; }

    [Required]
    public string Name { get; set; } = default!;

    public string? IntegrationId { get; set; }

    [UserHashIdConverter]
    public IEnumerable<ShiftEntitySelectDTO> Users { get; set; } = new List<ShiftEntitySelectDTO>();
}
