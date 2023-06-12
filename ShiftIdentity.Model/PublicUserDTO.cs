using ShiftSoftware.ShiftEntity.Model.Dtos;

namespace ShiftSoftware.ShiftIdentity.Model;
public class PublicUserListDTO : ShiftEntityListDTO
{
    [_UserHashId]
    public override string? ID { get; set; }
    public string Name { get; set; } = default!;
}
