using ShiftSoftware.ShiftEntity.Model.Dtos;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.User;

public class UserListDTO : ShiftEntityListDTO
{
    [_UserHashId]
    public override string? ID { get; set; }
    public string FullName { get; set; } = default!;
    public string Username { get; set; } = default!;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; }
}
