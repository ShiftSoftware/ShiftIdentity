
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Service;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Entities;

[TemporalShiftEntity]
[Table("Services", Schema = "ShiftIdentity")]
public class Service : ShiftEntity<Service>
{
    public string Name { get; set; } = default!;

}
