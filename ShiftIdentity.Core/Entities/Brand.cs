using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Flags;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("Brands", Schema = "ShiftIdentity")]
[ShiftEntityKeyAndName(nameof(ID), nameof(Name))]
public class Brand : ShiftEntity<Brand>, IEntityHasBrand<Brand>
{
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
    public long? BrandID { get; set; }
}
