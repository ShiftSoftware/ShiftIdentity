using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;

public class ResetPasswordDTO
{
    [Required]
    [MaxLength(255)]
    [DataType(DataType.Password)]
    [Password(
        requiredLength: 6,
        requiredUniqueChars: 3,
        requireDigit: true)]
    public string NewPassword { get; set; } = default!;

    [Compare(nameof(NewPassword))]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = default!;
}
