using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;

public class ChangePasswordDTO
{
    [Required]
    [MaxLength(255)]
    [DataType(DataType.Password)]
    [Password(
        requiredLength: 6,
        requiredUniqueChars: 3,
        requireDigit: true)]
    [NotEqualTo(nameof(CurrentPassword), ErrorMessage = $"{nameof(NewPassword)} can not be the same as {nameof(CurrentPassword)}")]
    public string NewPassword { get; set; } = default!;

    [Compare(nameof(NewPassword))]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = default!;

    [Required]
    [MaxLength(255)]
    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = default!;
}