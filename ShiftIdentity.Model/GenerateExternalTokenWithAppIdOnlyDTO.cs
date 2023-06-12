using System.ComponentModel.DataAnnotations;
using System;

namespace ShiftSoftware.ShiftIdentity.Model;

public class GenerateExternalTokenWithAppIdOnlyDTO
{
    [Required]
    public Guid AuthCode { get; set; } = default!;

    private string appId= default!;

    [Required]
    public string AppId
    {
        get { return appId.ToLower(); }
        set { appId = value.ToLower(); }
    }

    [Required]
    public string CodeVerifier { get; set; } = default!;
}
