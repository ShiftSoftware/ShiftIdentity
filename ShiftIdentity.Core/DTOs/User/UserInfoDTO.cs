using ShiftSoftware.ShiftEntity.Model.HashIds;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.User;

public class UserInfoDTO
{
    [UserHashIdConverter]
    public string? ID { get; set; }

    public string Username { get; set; } = default!;

    public string? Email { get; set; } = default!;

    public string? Phone { get; set; } = default!;


    public string FullName { get; set; } = default!;

    [DataType(DataType.Date)]
    public DateTime? BirthDate { get; set; }

    public string? PlainTextPassword { get; set; }
}
