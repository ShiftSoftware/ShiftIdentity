using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("Brands", Schema = "ShiftIdentity")]
public class Brand : ShiftEntity<Brand>
{
    public string Name { get; set; } = default!;
}
