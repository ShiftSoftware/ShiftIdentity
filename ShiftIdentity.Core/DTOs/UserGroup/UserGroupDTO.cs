using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.UserGroup;

public class UserGroupDTO : ShiftEntityViewAndUpsertDTO
{
    [UserGroupHashIdConverter]
    public override string? ID { get; set; }

    [Required]
    public string Name { get; set; } = default!;

    [UserHashIdConverter]
    public IEnumerable<ShiftEntitySelectDTO> Users { get; set; } = new List<ShiftEntitySelectDTO>();
}
