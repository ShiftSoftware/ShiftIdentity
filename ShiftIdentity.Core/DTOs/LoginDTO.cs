using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs;

public class LoginDTO
{
    [Required]
    [MaxLength(255)]
    public string Username { get; set; } = default!;

    [Required]
    [MaxLength(255)]
    public string Password { get; set; } = default!;
}
