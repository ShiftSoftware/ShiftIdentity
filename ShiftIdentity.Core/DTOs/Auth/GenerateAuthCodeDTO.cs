using System.ComponentModel.DataAnnotations;
namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Auth;

public class GenerateAuthCodeDTO
{
    [Required]
    public string AppId { get; set; } = default!;

    [Required]
    public string CodeChallenge { get; set; } = default!;

    public string? ReturnUrl { get; set; }
}
