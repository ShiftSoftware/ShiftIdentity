using ShiftSoftware.ShiftEntity.Model.HashIds;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.User;

public class UserInfoDTO : UserListDTO
{
    [DataType(DataType.Date)]
    public DateTime? BirthDate { get; set; }

    public string? PlainTextPassword { get; set; }
}
