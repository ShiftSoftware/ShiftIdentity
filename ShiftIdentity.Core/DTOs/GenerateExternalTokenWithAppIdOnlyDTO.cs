using System.ComponentModel.DataAnnotations;
using System;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs;


public class GenerateExternalTokenWithAppIdOnlyDTO
{
    [Required]
    public Guid AuthCode { get; set; }

    [Required]
    public string AppId { get; set; } = default!;

    [Required]
    public string CodeVerifier { get; set; } = default!;
}