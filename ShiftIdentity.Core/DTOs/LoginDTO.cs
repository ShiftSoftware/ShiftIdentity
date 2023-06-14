using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs;

public class LoginDTO
{
    private string username;

    [Required]
    [MaxLength(255)]
    public string Username
    {
        get { return username == null ? null : username.ToLower(); }
        set { username = value.ToLower(); }
    }

    [Required]
    [MaxLength(255)]
    public string Password { get; set; } = default!;
}
