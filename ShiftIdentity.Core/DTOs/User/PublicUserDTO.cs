using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.User;

public class PublicUserListDTO : ShiftEntityListDTO
{
    [UserHashIdConverter]
    public override string? ID { get; set; }
    public string Name { get; set; } = default!;
}