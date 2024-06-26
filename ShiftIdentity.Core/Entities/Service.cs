﻿using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("Services", Schema = "ShiftIdentity")]
public class Service : ShiftEntity<Service>
{
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
}
