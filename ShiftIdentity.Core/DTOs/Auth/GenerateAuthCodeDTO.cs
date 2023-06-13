using System.ComponentModel.DataAnnotations;
namespace ShiftSoftware.ShiftIdentity.Core.DTOs.Auth;

public class GenerateAuthCodeDTO
{
    private string appId = default!;

    [Required]
    public string AppId
    {
        get { return appId.ToLower(); }
        set { appId = value.ToLower(); }
    }


    [Required]
    public string CodeChallenge { get; set; } = default!;

    public string? ReturnUrl { get; set; }
}
