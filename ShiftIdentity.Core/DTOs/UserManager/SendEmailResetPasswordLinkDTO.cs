using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;

public class SendEmailResetPasswordLinkDTO
{
    [MaxLength(255)]
    [EmailAddress]
    [Required]
    public string? Email { get; set; } = default!;
}
