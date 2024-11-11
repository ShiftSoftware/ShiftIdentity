using ShiftSoftware.ShiftEntity.Model.Dtos;
using System.Collections;
using System.Collections.Generic;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.User;

public class UserImportDTO : ShiftEntityViewAndUpsertDTO
{
    public override string? ID { get; set; }
    public IEnumerable<UserImportUserDTO> Users { get; set; }
    public bool SendLoginInfoByEmail { get; set; }
}
