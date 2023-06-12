using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Model;

public class UserDataDTO : ShiftEntity.Model.Dtos.ShiftEntityDTO
{
    [_UserHashId]
    public override string? ID { get; set; }
    [Required]
    [MaxLength(255)]
    public string Username { get; set; }

    [MaxLength(255)]
    [EmailAddress]
    public string? Email { get; set; }

    [MaxLength(30)]
    [Phone]
    public string? Phone { get; set; }

    [Required]
    [MaxLength(255)]
    public string FullName { get; set; } = default!;

    private DateTime? birthDate;
    [DataType(DataType.Date)]
    public DateTime? BirthDate
    {
        get { return birthDate; }
        set { birthDate = value?.Date; }
    }
}
