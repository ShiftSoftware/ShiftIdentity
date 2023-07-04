using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations;
using ShiftSoftware.ShiftIdentity.Core.DTOs.App;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Entities;

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

    public static implicit operator AppDTO(App entity)
    {
        if (entity == null)
            return default!;

        return new AppDTO
        {
            CreateDate = entity.CreateDate,
            CreatedByUserID = entity.CreatedByUserID.ToString(),
            ID = entity.ID.ToString(),
            IsDeleted = entity.IsDeleted,
            LastSaveDate = entity.LastSaveDate,
            LastSavedByUserID = entity.LastSavedByUserID.ToString(),

            DisplayName = entity.DisplayName,
            AppId = entity.AppId,
            AppSecret = entity.AppSecret,
            Description = entity.Description,
            RedirectUri = entity.RedirectUri,
            //PostLogoutRedirectUri = entity.PostLogoutRedirectUri,
        };
    }
}
