using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;

public class EnrollTotpDTO
{
    [Required]
    public string Code { get; set; } = default!;
}
