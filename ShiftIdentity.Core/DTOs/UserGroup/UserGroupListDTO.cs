using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.UserGroup;

[ShiftEntityKeyAndName(nameof(ID), nameof(Name))]
public class UserGroupListDTO : ShiftEntityListDTO
{
    [UserGroupHashIdConverter]
    public override string? ID { get; set; }
    public string Name { get; set; } = default!;
}
