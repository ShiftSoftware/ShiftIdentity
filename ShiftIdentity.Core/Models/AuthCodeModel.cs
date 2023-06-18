using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ShiftSoftware.ShiftIdentity.Core.Models;

public class AuthCodeModel
{
    public Guid Code { get; set; }

    [JsonIgnore]
    public long UserID { get; set; }

    public string AppDisplayName { get; set; } = default!;

    [Required]
    public string AppId { get; set; } = default!;


    [JsonIgnore]
    public DateTime Expire { get; set; } = default!;

    [JsonIgnore]
    public string CodeChallenge { get; set; } = default!;

    public string RedirectUri { get; set; } = default!;
    public string ReturnUrl { get; set; } = default!;
}
