using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("Apps", Schema = "ShiftIdentity")]
public class App : ShiftEntity<App>
{
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

    //[Required]
    //[MaxLength(4000)]
    //public string PostLogoutRedirectUri { get; set; } = default!;

}
