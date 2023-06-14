using ShiftSoftware.ShiftEntity.Model.Dtos;
using System.ComponentModel.DataAnnotations;
using ShiftSoftware.ShiftEntity.Model.HashId;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.App;

public class AppDTO : ShiftEntityMixedDTO
{
    [JsonHashIdConverter<AppDTO>(5)]
    public override string? ID { get; set; }
    [Required]
    [MaxLength(255)]
    public string DisplayName { get; set; } = default!;

    [Required]
    [MaxLength(255)]
    public string AppId { get; set; } = default!;

    [MaxLength(255)]
    public string? AppSecret { get; set; }

    [MaxLength(4000)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(4000)]
    public string RedirectUri { get; set; } = default!;

    [Required]
    [MaxLength(4000)]
    public string PostLogoutRedirectUri { get; set; } = default!;
}
