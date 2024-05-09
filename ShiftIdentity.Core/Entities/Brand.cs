using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("Brands", Schema = "ShiftIdentity")]
[ShiftEntityKeyAndName(nameof(ID), nameof(Name))]
public class Brand : ShiftEntity<Brand>
{
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
}
