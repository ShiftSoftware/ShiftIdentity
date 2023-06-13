using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.Models;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs
{
    public class PublicUserListDTO : ShiftEntityListDTO
    {
        [_UserHashId]
        public override string ID { get; set; }
        public string Name { get; set; }
    }
}