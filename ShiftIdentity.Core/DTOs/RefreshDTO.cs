using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs;


public class RefreshDTO
{
    [Required]
    public string RefreshToken { get; set; } = default!;
}